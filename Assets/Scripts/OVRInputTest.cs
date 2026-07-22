using UnityEngine;

//standalone diagnostic, has nothing to do with the actual game
//just checks whether ovrinput is receiving anything at all in this project right now
//put this on any empty object in the scene temporarily, remove once triggers are confirmed working
public class OVRInputTest:MonoBehaviour
{
    void Update()
    {
        //logs unconditionally every frame, no threshold, so we see the real number even if its exactly 0
        float leftAxis = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.LTouch);
        float rightAxis = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.RTouch);

        //this tells us if the headset even sees the controllers as connected, separate from whether triggers are being pulled
        OVRInput.Controller connected = OVRInput.GetConnectedControllers();

        Debug.Log($"left {leftAxis:F3} right {rightAxis:F3} connected {connected}");
    }
}