using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ATADTRL.Environment
{
    public enum RackSlotState { Empty, Reserved, Occupied }

    [Serializable]
    public class ParcelInventoryRecord
    {
        public string parcelId;
        public string parcelNumber;
        public string barcode;
        public string details;
        public string category;
        public float massKg;
        public string sourceId;
        public string destinationId;

        public ParcelInventoryRecord Clone() => (ParcelInventoryRecord)MemberwiseClone();
    }

    [Serializable]
    public class RackSlot
    {
        public string slotId;
        public string rackId;
        public string category;
        public int levelIndex;
        public int bayIndex;
        public Vector3 storagePosition;
        public Vector3 approachPosition;
        public RackSlotState state;
        public RackSlotState initiallyState;
        public ParcelInventoryRecord parcel;
        public ParcelInventoryRecord initialParcel;

        // Legacy flags: a reservation is unavailable just like occupied stock.
        public bool occupied;
        public bool initiallyOccupied;

        [NonSerialized] public GameObject visual;
        [NonSerialized] public float visualRackWidth;

        public bool IsVacant => state == RackSlotState.Empty;
        public bool IsReserved => state == RackSlotState.Reserved;
        public bool HasParcel => state == RackSlotState.Occupied;
    }

    [Serializable]
    public class RackInventorySummary
    {
        public string rackId;
        public string rackName;
        public string category;
        public int capacity;
        public int occupiedCount;
        public int reservedCount;
        public int vacantCount;
    }

    /// <summary>
    /// Runtime inventory map. Each rack has four levels and three bays,
    /// producing twelve addressable and observable storage segments.
    /// </summary>
    public class WarehouseInventory : MonoBehaviour
    {
        public readonly List<RackSlot> Slots = new List<RackSlot>();
        public event Action InventoryChanged;

        public int TotalCapacity => Slots.Count;
        public int TotalOccupied => Slots.Count(slot => slot.state == RackSlotState.Occupied);
        public int TotalReserved => Slots.Count(slot => slot.state == RackSlotState.Reserved);
        public int TotalVacant => Slots.Count(slot => slot.state == RackSlotState.Empty);

        private readonly Dictionary<string, string> _rackCategories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Rack_0_3", "Electronics" }, { "Rack_5_3", "Apparel" },
            { "Rack_0_0", "Healthcare" },  { "Rack_5_0", "Food" },
            { "Rack_1_3", "Homeware" },    { "Rack_4_3", "Books" },
            { "Rack_1_0", "Toys" },        { "Rack_4_0", "Automotive" },
            { "Rack_2_3", "Tools" },       { "Rack_3_3", "Cosmetics" }
        };

        private static readonly Dictionary<string, string[]> ProductDescriptions =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "Electronics", new[] { "Wireless sensor kit", "Industrial tablet", "Motor controller", "Network switch" } },
                { "Apparel", new[] { "Protective work jacket", "Uniform carton", "Safety footwear", "Textile bundle" } },
                { "Healthcare", new[] { "First-aid supplies", "Medical consumables", "Protective equipment", "Sanitation pack" } },
                { "Food", new[] { "Dry-goods carton", "Sealed food case", "Beverage pack", "Shelf-stable supplies" } },
                { "Homeware", new[] { "Kitchenware carton", "Home storage set", "Small appliance", "Household supplies" } },
                { "Books", new[] { "Reference books", "Textbook carton", "Printed manuals", "Archived publications" } },
                { "Toys", new[] { "Educational toy set", "Board-game carton", "Model kit", "Activity supplies" } },
                { "Automotive", new[] { "Service components", "Vehicle filters", "Workshop consumables", "Replacement parts" } },
                { "Tools", new[] { "Hand-tool set", "Power-tool accessories", "Fastener case", "Maintenance equipment" } },
                { "Cosmetics", new[] { "Personal-care carton", "Sealed cosmetic case", "Hygiene products", "Retail display stock" } },
                { "General", new[] { "General warehouse stock", "Mixed inventory carton", "Operations supplies", "Unclassified goods" } }
            };

        private Material _occupiedMaterial;
        private Material _reservedMaterial;
        private Material _vacantMaterial;
        private int _runtimeParcelSequence;

        public string GetCategoryForRack(string rackId)
        {
            return !string.IsNullOrWhiteSpace(rackId) && _rackCategories.TryGetValue(rackId, out string category)
                ? category : "General";
        }

        public string GetRackDisplayName(string rackId)
        {
            string category = GetCategoryForRack(rackId);
            return string.Equals(category, "General", StringComparison.OrdinalIgnoreCase)
                ? $"General Storage ({rackId})" : $"{category} Rack ({rackId})";
        }

        public void RegisterRack(string rackId, string category, Transform rack, float rackWidth, float rackLength, float rackHeight)
        {
            if (string.IsNullOrWhiteSpace(rackId) || rack == null) return;
            category = string.IsNullOrWhiteSpace(category) ? GetCategoryForRack(rackId) : category.Trim();
            _rackCategories[rackId] = category;

            // Idempotent when a custom environment rebuild reuses this component.
            foreach (RackSlot oldSlot in Slots.Where(slot => RackMatches(slot.rackId, rackId)).ToList())
            {
                DestroyInventoryObject(oldSlot.visual);
                Slots.Remove(oldSlot);
            }

            float rackX = rack.position.x;
            float approachX = rackX < 0f ? rackX - rackWidth * 0.5f - 1.05f : rackX + rackWidth * 0.5f + 1.05f;
            float[] levels = { rackHeight * 0.17f, rackHeight * 0.455f, rackHeight * 0.74f, rackHeight * 0.8875f };
            float[] bays = { -rackLength * 0.2833f, 0f, rackLength * 0.2833f };
            int slotNumber = 0;

            for (int levelIndex = 0; levelIndex < levels.Length; levelIndex++)
            {
                for (int bayIndex = 0; bayIndex < bays.Length; bayIndex++)
                {
                    slotNumber++;
                    bool stocked = slotNumber % 3 != 1;
                    var slot = new RackSlot
                    {
                        slotId = $"{rackId}-S{slotNumber:D2}", rackId = rackId, category = category,
                        levelIndex = levelIndex + 1, bayIndex = bayIndex + 1,
                        storagePosition = rack.position + new Vector3(0f, levels[levelIndex], bays[bayIndex]),
                        approachPosition = new Vector3(approachX, 0f, rack.position.z + bays[bayIndex]),
                        state = stocked ? RackSlotState.Occupied : RackSlotState.Empty,
                        initiallyState = stocked ? RackSlotState.Occupied : RackSlotState.Empty,
                        occupied = stocked, initiallyOccupied = stocked, visualRackWidth = rackWidth
                    };
                    if (stocked)
                    {
                        slot.initialParcel = CreateSeededInitialParcel(slot);
                        slot.parcel = slot.initialParcel.Clone();
                    }
                    slot.visual = CreateSlotVisual(rack, slot);
                    Slots.Add(slot);
                }
            }
            InventoryChanged?.Invoke();
        }

        // Legacy API retained. A slot now stays Reserved until placement commits.
        public RackSlot ReserveFirstAvailableSlot(string category) => ReserveFirstAvailableSlot(category, null, null);

        public RackSlot ReserveFirstAvailableSlot(string category, ParcelInventoryRecord parcel) =>
            ReserveFirstAvailableSlot(category, parcel, null);

        public RackSlot ReserveFirstAvailableSlot(string category, ParcelInventoryRecord parcel, string preferredRackId)
        {
            RackSlot slot = FindFirstAvailableSlot(category, preferredRackId);
            if (slot == null) return null;
            ApplySlotState(slot, RackSlotState.Reserved, PrepareParcelForSlot(parcel, slot, "Reserved parcel"), true);
            return slot;
        }

        public bool TryReserveFirstAvailableSlot(string category, ParcelInventoryRecord parcel, out RackSlot slot,
            string preferredRackId = null)
        {
            slot = ReserveFirstAvailableSlot(category, parcel, preferredRackId);
            return slot != null;
        }

        public bool ReserveSlot(string slotId, ParcelInventoryRecord parcel)
        {
            if (!TryGetSlot(slotId, out RackSlot slot) || slot.state != RackSlotState.Empty) return false;
            ApplySlotState(slot, RackSlotState.Reserved, PrepareParcelForSlot(parcel, slot, "Reserved parcel"), true);
            return true;
        }

        public bool CommitPlacement(RackSlot slot, ParcelInventoryRecord parcel = null)
        {
            if (slot == null || slot.state == RackSlotState.Occupied) return false;
            ApplySlotState(slot, RackSlotState.Occupied,
                PrepareParcelForSlot(parcel ?? slot.parcel, slot, "Stored parcel"), true);
            return true;
        }

        public bool CommitPlacement(string slotId, ParcelInventoryRecord parcel = null) =>
            TryGetSlot(slotId, out RackSlot slot) && CommitPlacement(slot, parcel);

        public bool CancelReservation(RackSlot slot)
        {
            if (slot == null || slot.state != RackSlotState.Reserved) return false;
            ApplySlotState(slot, RackSlotState.Empty, null, true);
            return true;
        }

        public bool CancelReservation(string slotId) => TryGetSlot(slotId, out RackSlot slot) && CancelReservation(slot);

        public int EmptySlotCount(string category) => Slots.Count(slot =>
            CategoryMatches(slot.category, category) && slot.state == RackSlotState.Empty);

        public int EmptySlotCountForRack(string rackId) => Slots.Count(slot =>
            RackMatches(slot.rackId, rackId) && slot.state == RackSlotState.Empty);

        public int ReservedSlotCount(string category = null) => Slots.Count(slot =>
            (string.IsNullOrWhiteSpace(category) || CategoryMatches(slot.category, category)) &&
            slot.state == RackSlotState.Reserved);

        public int OccupiedSlotCount(string category = null) => Slots.Count(slot =>
            (string.IsNullOrWhiteSpace(category) || CategoryMatches(slot.category, category)) &&
            slot.state == RackSlotState.Occupied);

        public RackSlot FindFirstAvailableSlot(string category, string preferredRackId = null)
        {
            IEnumerable<RackSlot> candidates = Slots.Where(slot =>
                slot.state == RackSlotState.Empty && CategoryMatches(slot.category, category));
            if (!string.IsNullOrWhiteSpace(preferredRackId))
                candidates = candidates.Where(slot => RackMatches(slot.rackId, preferredRackId));
            return candidates.OrderBy(slot => slot.rackId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(slot => slot.levelIndex).ThenBy(slot => slot.bayIndex).FirstOrDefault();
        }

        public bool TryGetSlot(string slotId, out RackSlot slot)
        {
            slot = Slots.FirstOrDefault(candidate =>
                string.Equals(candidate.slotId, slotId, StringComparison.OrdinalIgnoreCase));
            return slot != null;
        }

        public List<string> GetRackIds() => Slots.Select(slot => slot.rackId)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();

        public List<RackSlot> GetSlotsForRack(string rackId) => Slots.Where(slot => RackMatches(slot.rackId, rackId))
            .OrderBy(slot => slot.levelIndex).ThenBy(slot => slot.bayIndex).ToList();

        public RackInventorySummary GetRackSummary(string rackId)
        {
            List<RackSlot> rackSlots = GetSlotsForRack(rackId);
            if (rackSlots.Count == 0) return null;
            return new RackInventorySummary
            {
                rackId = rackId, rackName = GetRackDisplayName(rackId), category = rackSlots[0].category,
                capacity = rackSlots.Count,
                occupiedCount = rackSlots.Count(slot => slot.state == RackSlotState.Occupied),
                reservedCount = rackSlots.Count(slot => slot.state == RackSlotState.Reserved),
                vacantCount = rackSlots.Count(slot => slot.state == RackSlotState.Empty)
            };
        }

        public bool TryGetRackSummary(string rackId, out RackInventorySummary summary)
        {
            summary = GetRackSummary(rackId);
            return summary != null;
        }

        public List<RackInventorySummary> GetRackSummaries() => GetRackIds()
            .Select(GetRackSummary).Where(summary => summary != null).ToList();

        public void ReleaseSlot(RackSlot slot)
        {
            if (slot != null) ApplySlotState(slot, RackSlotState.Empty, null, true);
        }

        public bool ReleaseSlot(string slotId)
        {
            if (!TryGetSlot(slotId, out RackSlot slot)) return false;
            ReleaseSlot(slot);
            return true;
        }

        public bool TryRemoveParcel(string slotId, out ParcelInventoryRecord parcel)
        {
            parcel = null;
            if (!TryGetSlot(slotId, out RackSlot slot) || slot.state != RackSlotState.Occupied) return false;
            parcel = slot.parcel?.Clone();
            ReleaseSlot(slot);
            return true;
        }

        public void ResetToInitialLayout()
        {
            foreach (RackSlot slot in Slots)
                ApplySlotState(slot, slot.initiallyState, slot.initialParcel?.Clone(), false);
            _runtimeParcelSequence = 0;
            InventoryChanged?.Invoke();
        }

        private ParcelInventoryRecord CreateSeededInitialParcel(RackSlot slot)
        {
            uint seed = StableHash(slot.slotId);
            string slotCode = new string(slot.slotId.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            return new ParcelInventoryRecord
            {
                parcelId = $"INV-{slotCode}",
                parcelNumber = $"PCL-{slotCode}",
                barcode = $"WH-{BuildCategoryCode(slot.category)}-{slotCode}",
                details = GetSeededProductDescription(slot.category, seed), category = slot.category,
                massKg = Mathf.Round((0.75f + seed % 1125u / 100f) * 100f) / 100f,
                sourceId = "Inbound Dock", destinationId = slot.slotId
            };
        }

        private ParcelInventoryRecord PrepareParcelForSlot(ParcelInventoryRecord parcel, RackSlot slot, string defaultDetails)
        {
            ParcelInventoryRecord prepared = parcel?.Clone() ?? new ParcelInventoryRecord();
            if (string.IsNullOrWhiteSpace(prepared.parcelId))
            {
                _runtimeParcelSequence++;
                prepared.parcelId = $"RUN-{_runtimeParcelSequence:D5}";
            }
            if (string.IsNullOrWhiteSpace(prepared.parcelNumber)) prepared.parcelNumber = prepared.parcelId;
            if (string.IsNullOrWhiteSpace(prepared.barcode)) prepared.barcode = $"ATD-{prepared.parcelId}";
            if (string.IsNullOrWhiteSpace(prepared.details)) prepared.details = defaultDetails;
            if (string.IsNullOrWhiteSpace(prepared.category)) prepared.category = slot.category;
            if (prepared.massKg <= 0f) prepared.massKg = 1f;
            if (string.IsNullOrWhiteSpace(prepared.sourceId)) prepared.sourceId = "Unspecified Source";
            prepared.destinationId = slot.slotId;
            return prepared;
        }

        private static string GetSeededProductDescription(string category, uint seed)
        {
            if (!ProductDescriptions.TryGetValue(category ?? "General", out string[] descriptions))
                descriptions = ProductDescriptions["General"];
            return descriptions[seed % (uint)descriptions.Length];
        }

        private static string BuildCategoryCode(string category)
        {
            string code = new string((category ?? "GEN").Where(char.IsLetterOrDigit).Take(3).ToArray()).ToUpperInvariant();
            return code.PadRight(3, 'X');
        }

        private static uint StableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                foreach (char character in value ?? string.Empty) { hash ^= character; hash *= 16777619u; }
                return hash;
            }
        }

        private void ApplySlotState(RackSlot slot, RackSlotState state, ParcelInventoryRecord parcel, bool notify)
        {
            slot.state = state;
            slot.occupied = state != RackSlotState.Empty;
            slot.parcel = state == RackSlotState.Empty ? null : parcel;
            UpdateSlotVisual(slot);
            if (notify) InventoryChanged?.Invoke();
        }

        private GameObject CreateSlotVisual(Transform rack, RackSlot slot)
        {
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.transform.SetParent(rack, true);
            Collider collider = visual.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
            slot.visual = visual;
            UpdateSlotVisual(slot);
            return visual;
        }

        private void UpdateSlotVisual(RackSlot slot)
        {
            if (slot.visual == null) return;
            float width = slot.visualRackWidth > 0f ? slot.visualRackWidth : 1.2f;
            Renderer renderer = slot.visual.GetComponent<Renderer>();
            if (slot.state == RackSlotState.Occupied)
            {
                slot.visual.name = slot.slotId + "_Occupied";
                slot.visual.transform.position = slot.storagePosition;
                slot.visual.transform.localScale = new Vector3(width * 0.62f, 0.38f, 0.82f);
                if (renderer != null) renderer.sharedMaterial = GetOccupiedMaterial();
            }
            else if (slot.state == RackSlotState.Reserved)
            {
                slot.visual.name = slot.slotId + "_Reserved";
                slot.visual.transform.position = slot.storagePosition + Vector3.down * 0.17f;
                slot.visual.transform.localScale = new Vector3(width * 0.66f, 0.07f, 0.88f);
                if (renderer != null) renderer.sharedMaterial = GetReservedMaterial();
            }
            else
            {
                slot.visual.name = slot.slotId + "_Vacant";
                slot.visual.transform.position = slot.storagePosition + Vector3.down * 0.19f;
                slot.visual.transform.localScale = new Vector3(width * 0.68f, 0.035f, 0.90f);
                if (renderer != null) renderer.sharedMaterial = GetVacantMaterial();
            }
        }

        private Material GetOccupiedMaterial() => _occupiedMaterial != null ? _occupiedMaterial :
            (_occupiedMaterial = CreateMaterial("Inventory Occupied - Kraft Cardboard", new Color(0.55f, 0.34f, 0.15f), 0f, 0.18f));

        private Material GetReservedMaterial() => _reservedMaterial != null ? _reservedMaterial :
            (_reservedMaterial = CreateMaterial("Inventory Reserved - Amber", new Color(1f, 0.58f, 0.06f), 0.05f, 0.34f));

        private Material GetVacantMaterial() => _vacantMaterial != null ? _vacantMaterial :
            (_vacantMaterial = CreateMaterial("Inventory Vacant - Green", new Color(0.08f, 0.68f, 0.30f), 0.05f, 0.28f));

        private static Material CreateMaterial(string materialName, Color color, float metallic, float smoothness)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Diffuse");
            var material = new Material(shader) { name = materialName, color = color };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
            return material;
        }

        private void OnDestroy()
        {
            DestroyInventoryObject(_occupiedMaterial);
            DestroyInventoryObject(_reservedMaterial);
            DestroyInventoryObject(_vacantMaterial);
        }

        private static bool CategoryMatches(string left, string right) => string.Equals(
            left, string.IsNullOrWhiteSpace(right) ? "General" : right, StringComparison.OrdinalIgnoreCase);

        private static bool RackMatches(string left, string right) =>
            string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        private static void DestroyInventoryObject(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target); else DestroyImmediate(target);
        }
    }
}
