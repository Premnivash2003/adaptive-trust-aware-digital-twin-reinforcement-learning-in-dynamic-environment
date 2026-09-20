"""Train the A1 blind-corner navigation policy with clipped PPO.

The trainer is dependency-light (NumPy only) so the learned policy can be
reproduced on the project machine.  It creates a compact MLP exported as JSON
for deterministic Unity inference and a per-update training log.
"""

from __future__ import annotations

import argparse
import csv
import json
from pathlib import Path

import numpy as np


ACTIONS = ["Forward", "SlowDown", "Wait", "TurnLeft", "TurnRight"]
INPUTS = [
    "collision_risk",
    "minimum_separation_normalized",
    "time_to_closest_approach_normalized",
    "overall_trust",
    "carrying",
    "blind_corner_exposure",
    "safety_hold_active",
    "route_progress",
]


class Adam:
    def __init__(self, params: list[np.ndarray], lr: float):
        self.params = params
        self.lr = lr
        self.m = [np.zeros_like(p) for p in params]
        self.v = [np.zeros_like(p) for p in params]
        self.t = 0

    def step(self, grads: list[np.ndarray]) -> None:
        self.t += 1
        for i, (p, g) in enumerate(zip(self.params, grads)):
            g = np.clip(g, -1.0, 1.0)
            self.m[i] = 0.9 * self.m[i] + 0.1 * g
            self.v[i] = 0.999 * self.v[i] + 0.001 * (g * g)
            mh = self.m[i] / (1.0 - 0.9**self.t)
            vh = self.v[i] / (1.0 - 0.999**self.t)
            p -= self.lr * mh / (np.sqrt(vh) + 1e-8)


class ActorCritic:
    def __init__(self, rng: np.random.Generator, inputs: int = 8, hidden: int = 32, actions: int = 5):
        self.w1 = rng.normal(0.0, np.sqrt(2.0 / inputs), (inputs, hidden)).astype(np.float32)
        self.b1 = np.zeros(hidden, np.float32)
        self.wp = rng.normal(0.0, 0.08, (hidden, actions)).astype(np.float32)
        self.bp = np.zeros(actions, np.float32)
        self.wv = rng.normal(0.0, 0.08, (hidden, 1)).astype(np.float32)
        self.bv = np.zeros(1, np.float32)
        self.actor_optim = Adam([self.w1, self.b1, self.wp, self.bp], 2.5e-4)
        self.critic_optim = Adam([self.wv, self.bv], 8.0e-4)

    def forward(self, x: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
        h = np.tanh(x @ self.w1 + self.b1)
        logits = h @ self.wp + self.bp
        logits -= logits.max(axis=1, keepdims=True)
        probs = np.exp(logits)
        probs /= probs.sum(axis=1, keepdims=True)
        values = (h @ self.wv + self.bv)[:, 0]
        return h, probs, values

    def update_actor(self, x: np.ndarray, actions: np.ndarray, old_logp: np.ndarray,
                     advantage: np.ndarray, clip: float) -> tuple[float, float]:
        h, probs, _ = self.forward(x)
        chosen = np.maximum(probs[np.arange(len(x)), actions], 1e-8)
        new_logp = np.log(chosen)
        ratio = np.exp(new_logp - old_logp)
        clipped_ratio = np.clip(ratio, 1.0 - clip, 1.0 + clip)
        surrogate = np.minimum(ratio * advantage, clipped_ratio * advantage)
        active = ((advantage >= 0.0) & (ratio <= 1.0 + clip)) | ((advantage < 0.0) & (ratio >= 1.0 - clip))
        coeff = np.where(active, -advantage * ratio / len(x), 0.0).astype(np.float32)
        one_hot = np.eye(probs.shape[1], dtype=np.float32)[actions]
        dlogits = coeff[:, None] * (one_hot - probs)
        gwp = h.T @ dlogits
        gbp = dlogits.sum(axis=0)
        dh = dlogits @ self.wp.T
        dz = dh * (1.0 - h * h)
        gw1 = x.T @ dz
        gb1 = dz.sum(axis=0)
        self.actor_optim.step([gw1, gb1, gwp, gbp])
        entropy = float((-probs * np.log(np.maximum(probs, 1e-8))).sum(axis=1).mean())
        return float(-surrogate.mean()), entropy

    def update_critic(self, x: np.ndarray, returns: np.ndarray) -> float:
        h, _, values = self.forward(x)
        error = values - returns
        dvalue = (2.0 / len(x)) * error
        gwv = h.T @ dvalue[:, None]
        gbv = np.array([dvalue.sum()], np.float32)
        self.critic_optim.step([gwv, gbv])
        return float((error * error).mean())


class A1VectorEnvironment:
    def __init__(self, count: int, rng: np.random.Generator):
        self.n = count
        self.rng = rng
        self.progress = np.zeros(count, np.float32)
        self.hazard_ticks = np.zeros(count, np.int32)
        self.steps = np.zeros(count, np.int32)
        self.hold = np.zeros(count, np.float32)
        self.done = np.ones(count, bool)
        self.episode_return = np.zeros(count, np.float32)
        self.reset(np.arange(count))

    def reset(self, indices: np.ndarray) -> None:
        if len(indices) == 0:
            return
        self.progress[indices] = self.rng.uniform(0.0, 0.08, len(indices))
        self.hazard_ticks[indices] = self.rng.integers(12, 31, len(indices))
        self.steps[indices] = 0
        self.hold[indices] = 0.0
        self.done[indices] = False
        self.episode_return[indices] = 0.0

    def observe(self) -> np.ndarray:
        distance = np.maximum(0.0, 0.56 - self.progress)
        exposure = ((self.progress >= 0.32) & (self.progress <= 0.72)).astype(np.float32)
        hazard = (self.hazard_ticks > 0).astype(np.float32)
        risk = np.clip(np.exp(-distance * 9.0) * exposure * hazard, 0.0, 1.0)
        separation = np.clip(1.0 - risk * 0.92, 0.04, 1.0)
        ttc = np.clip(distance / 0.40, 0.0, 1.0)
        trust = self.rng.uniform(0.82, 1.0, self.n).astype(np.float32)
        return np.stack([
            risk, separation, ttc, trust,
            np.ones(self.n, np.float32), exposure, self.hold,
            np.clip(self.progress, 0.0, 1.0),
        ], axis=1).astype(np.float32)

    def step(self, actions: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray]:
        before = self.progress.copy()
        risk_before = self.observe()[:, 0]
        delta = np.choose(actions, [0.050, 0.022, 0.0, 0.012, 0.012]).astype(np.float32)
        self.progress += delta
        self.hazard_ticks = np.maximum(0, self.hazard_ticks - 1)
        self.steps += 1
        self.hold = (actions == 2).astype(np.float32)

        entered_conflict = (self.progress >= 0.50) & (self.progress <= 0.69)
        collision = entered_conflict & (self.hazard_ticks > 0) & ((actions == 0) | ((actions == 1) & (risk_before > 0.72)))
        success = self.progress >= 1.0
        timeout = self.steps >= 64
        done = collision | success | timeout

        reward = -0.025 + (self.progress - before) * 8.0
        reward += np.where((actions == 2) & (risk_before > 0.30), 0.32, 0.0)
        reward -= np.where((actions == 2) & (risk_before <= 0.30), 0.12, 0.0)
        reward -= np.where(((actions == 3) | (actions == 4)), 0.16, 0.0)
        reward -= np.where((actions == 0) & (risk_before > 0.30), 2.5 * risk_before, 0.0)
        reward -= np.where((actions == 1) & (risk_before > 0.55), 0.8 * risk_before, 0.0)
        reward += np.where(success, 20.0, 0.0)
        reward -= np.where(collision, 18.0, 0.0)
        reward -= np.where(timeout & ~success, 2.0, 0.0)
        self.episode_return += reward.astype(np.float32)
        self.done = done
        info = np.stack([collision, success], axis=1)
        return self.observe(), reward.astype(np.float32), done, info


def evaluate(model: ActorCritic, rng: np.random.Generator, episodes: int = 512) -> tuple[float, float, float]:
    env = A1VectorEnvironment(episodes, rng)
    total = np.zeros(episodes, np.float32)
    finished = np.zeros(episodes, bool)
    collision = np.zeros(episodes, bool)
    success = np.zeros(episodes, bool)
    for _ in range(80):
        state = env.observe()
        _, probs, _ = model.forward(state)
        action = probs.argmax(axis=1)
        _, reward, done, info = env.step(action)
        active = ~finished
        total[active] += reward[active]
        collision |= info[:, 0].astype(bool) & active
        success |= info[:, 1].astype(bool) & active
        finished |= done
        if finished.all():
            break
    return float(total.mean()), float(success.mean()), float(collision.mean())


def train(output: Path, metrics: Path, seed: int, updates: int) -> None:
    rng = np.random.default_rng(seed)
    model = ActorCritic(rng)
    env = A1VectorEnvironment(96, rng)
    horizon = 64
    gamma, lam, clip = 0.995, 0.95, 0.20
    rows: list[dict[str, float | int]] = []
    completed_returns: list[float] = []
    total_steps = 0

    for update in range(1, updates + 1):
        states = np.zeros((horizon, env.n, 8), np.float32)
        actions = np.zeros((horizon, env.n), np.int32)
        rewards = np.zeros((horizon, env.n), np.float32)
        dones = np.zeros((horizon, env.n), np.float32)
        values = np.zeros((horizon, env.n), np.float32)
        logps = np.zeros((horizon, env.n), np.float32)
        collisions = successes = 0

        for t in range(horizon):
            state = env.observe()
            _, probs, value = model.forward(state)
            cumulative = np.cumsum(probs, axis=1)
            action = (rng.random(env.n)[:, None] > cumulative).sum(axis=1).astype(np.int32)
            action = np.minimum(action, len(ACTIONS) - 1)
            next_state, reward, done, info = env.step(action)
            states[t], actions[t], rewards[t], dones[t], values[t] = state, action, reward, done, value
            logps[t] = np.log(np.maximum(probs[np.arange(env.n), action], 1e-8))
            collisions += int(info[:, 0].sum())
            successes += int(info[:, 1].sum())
            finished = np.where(done)[0]
            if len(finished):
                completed_returns.extend(env.episode_return[finished].tolist())
                env.reset(finished)

        _, _, next_value = model.forward(env.observe())
        advantages = np.zeros_like(rewards)
        gae = np.zeros(env.n, np.float32)
        for t in range(horizon - 1, -1, -1):
            nonterminal = 1.0 - dones[t]
            delta = rewards[t] + gamma * next_value * nonterminal - values[t]
            gae = delta + gamma * lam * nonterminal * gae
            advantages[t] = gae
            next_value = values[t]
        returns = advantages + values

        x = states.reshape(-1, 8)
        a = actions.reshape(-1)
        old_logp = logps.reshape(-1)
        adv = advantages.reshape(-1)
        ret = returns.reshape(-1)
        adv = (adv - adv.mean()) / (adv.std() + 1e-8)
        indices = np.arange(len(x))
        policy_losses, value_losses, entropies = [], [], []
        for _ in range(6):
            rng.shuffle(indices)
            for start in range(0, len(indices), 512):
                batch = indices[start:start + 512]
                pl, ent = model.update_actor(x[batch], a[batch], old_logp[batch], adv[batch], clip)
                vl = model.update_critic(x[batch], ret[batch])
                policy_losses.append(pl)
                value_losses.append(vl)
                entropies.append(ent)

        total_steps += horizon * env.n
        mean_reward, eval_success, eval_collision = evaluate(model, rng, 256)
        row = {
            "update": update,
            "timesteps": total_steps,
            "mean_episode_reward": round(float(np.mean(completed_returns[-512:])) if completed_returns else 0.0, 6),
            "evaluation_reward": round(mean_reward, 6),
            "success_rate": round(eval_success, 6),
            "collision_rate": round(eval_collision, 6),
            "policy_loss": round(float(np.mean(policy_losses)), 6),
            "value_loss": round(float(np.mean(value_losses)), 6),
            "entropy": round(float(np.mean(entropies)), 6),
        }
        rows.append(row)
        if update == 1 or update % 10 == 0 or update == updates:
            print(f"update={update:03d} steps={total_steps:7d} reward={mean_reward:7.3f} "
                  f"success={eval_success:.3f} collision={eval_collision:.3f} entropy={row['entropy']:.3f}")

    eval_reward, success_rate, collision_rate = evaluate(model, np.random.default_rng(seed + 991), 2048)
    payload = {
        "policyId": "A1_PPO_V1",
        "algorithm": "PPO_CLIPPED",
        "version": 1,
        "seed": seed,
        "trainingUpdates": updates,
        "trainingTimesteps": total_steps,
        "evaluationEpisodes": 2048,
        "evaluationMeanReward": round(eval_reward, 6),
        "evaluationSuccessRate": round(success_rate, 6),
        "evaluationCollisionRate": round(collision_rate, 6),
        "inputSize": 8,
        "hiddenSize": 32,
        "actionSize": 5,
        "inputNames": INPUTS,
        "actionNames": ACTIONS,
        "inputMean": [0.0] * 8,
        "inputScale": [1.0] * 8,
        "w1": model.w1.reshape(-1).round(8).tolist(),
        "b1": model.b1.round(8).tolist(),
        "w2": model.wp.reshape(-1).round(8).tolist(),
        "b2": model.bp.round(8).tolist(),
    }
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(payload, indent=2), encoding="utf-8")
    metrics.parent.mkdir(parents=True, exist_ok=True)
    with metrics.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(rows[0].keys()))
        writer.writeheader()
        writer.writerows(rows)
    print(f"saved_model={output}")
    print(f"saved_metrics={metrics}")
    print(f"final_success={success_rate:.4f} final_collision={collision_rate:.4f}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--metrics", type=Path, required=True)
    parser.add_argument("--seed", type=int, default=20260920)
    parser.add_argument("--updates", type=int, default=100)
    args = parser.parse_args()
    train(args.output, args.metrics, args.seed, args.updates)
