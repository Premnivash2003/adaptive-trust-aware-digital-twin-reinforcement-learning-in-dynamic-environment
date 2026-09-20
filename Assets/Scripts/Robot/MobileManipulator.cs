using UnityEngine;
using ATADTRL.Environment;

namespace ATADTRL.Robot
{
    // Kinematic lift-column / telescopic manipulator used by the simulation.
    // It models reach, slew, vertical/extension rates and a gripper latch;
    // it is not a torque controller or a certified load-stability model.
    public sealed class MobileManipulator : MonoBehaviour
    {
        private Transform _base, _lift, _boom, _tool, _leftFinger, _rightFinger;
        private float _height = 0.9f, _reach = 0.25f, _yaw;
        public Transform Tool => _tool;
        public bool GripperClosed { get; private set; }
        public float MaximumReach => 2.5f;
        public float MaximumPayloadKg => 8f;
        public Vector3 StowPoint => transform.TransformPoint(new Vector3(0f, 0.90f, -0.48f));
        public void Initialize()
        {
            if (_tool != null) return;
            Transform old = transform.Find("Detailed visual/AMR Arm");
            if (old != null) old.gameObject.SetActive(false);
            _base = new GameObject("Mobile handling column").transform; _base.SetParent(transform, false);
            Piece(_base,"Lift column", new Vector3(0f,0.72f,0.08f),new Vector3(0.14f,0.8f,0.15f),new Color(0.28f,0.32f,0.34f));
            _lift=new GameObject("Slew carriage").transform; _lift.SetParent(_base,false);
            _boom=Piece(_lift,"Telescopic beam",Vector3.zero,new Vector3(0.10f,0.09f,0.2f),new Color(0.56f,0.58f,0.60f));
            _tool=new GameObject("Parallel gripper").transform; _tool.SetParent(_lift,false);
            Piece(_tool,"Gripper housing",Vector3.zero,new Vector3(0.34f,0.08f,0.16f),new Color(0.9f,0.55f,0.1f));
            _leftFinger=Piece(_tool,"Jaw L",new Vector3(-0.30f,-0.10f,0f),new Vector3(0.035f,0.22f,0.13f),Color.gray);
            _rightFinger=Piece(_tool,"Jaw R",new Vector3(0.30f,-0.10f,0f),new Vector3(0.035f,0.22f,0.13f),Color.gray);
            ApplyPose();
        }
        public bool IsReachable(Vector3 point)
        {
            Vector3 p=transform.InverseTransformPoint(point);
            return new Vector2(p.x,p.z).magnitude<=MaximumReach && p.y>=0.42f && p.y<=2.15f;
        }
        public bool MoveTool(Vector3 point, bool close, float deltaTime)
        {
            Initialize(); if (!IsReachable(point)) return false;
            Vector3 p=transform.InverseTransformPoint(point);
            float desiredYaw=Mathf.Atan2(p.x,p.z)*Mathf.Rad2Deg;
            // Withdraw from the shelf before slewing; establish the shelf
            // height before inserting the beam through its opening.
            if (Mathf.Abs(Mathf.DeltaAngle(_yaw,desiredYaw))>8f && _reach>0.30f)
                _reach=Mathf.MoveTowards(_reach,0.25f,0.7f*deltaTime);
            else
            {
                _yaw=Mathf.MoveTowardsAngle(_yaw,desiredYaw,75f*deltaTime);
                _height=Mathf.MoveTowards(_height,p.y,0.55f*deltaTime);
                if (Mathf.Abs(_height-p.y)<0.04f && Mathf.Abs(Mathf.DeltaAngle(_yaw,desiredYaw))<4f)
                    _reach=Mathf.MoveTowards(_reach,new Vector2(p.x,p.z).magnitude,0.7f*deltaTime);
            }
            GripperClosed=close; ApplyPose();
            return Vector3.Distance(_tool.position,point)<0.045f;
        }
        public void OpenGripper() { GripperClosed=false; ApplyPose(); }
        private void ApplyPose()
        {
            if (_tool==null) return;
            _lift.localPosition=Vector3.up*_height; _lift.localRotation=Quaternion.Euler(0f,_yaw,0f);
            _boom.localPosition=Vector3.forward*_reach*0.5f; _boom.localScale=new Vector3(0.10f,0.09f,Mathf.Max(0.15f,_reach));
            _tool.localPosition=Vector3.forward*_reach;
            float span=GripperClosed ? 0.265f : 0.33f;
            _leftFinger.localPosition=new Vector3(-span,-0.10f,0f); _rightFinger.localPosition=new Vector3(span,-0.10f,0f);
        }
        private static Transform Piece(Transform parent,string name,Vector3 pos,Vector3 scale,Color color)
        {
            GameObject p=GameObject.CreatePrimitive(PrimitiveType.Cube); p.name=name; p.transform.SetParent(parent,false);
            p.transform.localPosition=pos;p.transform.localScale=scale;p.GetComponent<Collider>().enabled=false;
            p.GetComponent<Renderer>().material.color=color;return p.transform;
        }
    }
}
