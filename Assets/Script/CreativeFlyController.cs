using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class CreativeFlyController : UdonSharpBehaviour // ← ここをファイル名と合わせる！
{
    [Header("Settings")]
    public float flySpeed = 6.0f;
    public float verticalSpeed = 4.0f;

    private VRCPlayerApi localPlayer;
    private bool isFlying = false;

    void Start()
    {
        localPlayer = Networking.LocalPlayer;
    }

    void Update()
    {
        if (localPlayer == null || !localPlayer.isLocal) return;

        // Tabキーで飛行モード切替
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            ToggleFlight();
        }

        if (isFlying)
        {
            ProcessFlightMovement();
        }
    }

    private void ToggleFlight()
    {
        isFlying = !isFlying;

        if (isFlying)
        {
            localPlayer.SetGravityStrength(0f);
            localPlayer.SetVelocity(Vector3.zero);
        }
        else
        {
            localPlayer.SetGravityStrength(1.0f);
        }
    }

    private void ProcessFlightMovement()
    {
        float h = 0f;
        float v = 0f;
        float upDown = 0f;

        if (Input.GetKey(KeyCode.W)) v += 1f;
        if (Input.GetKey(KeyCode.S)) v -= 1f;
        if (Input.GetKey(KeyCode.D)) h += 1f;
        if (Input.GetKey(KeyCode.A)) h -= 1f;

        if (Input.GetKey(KeyCode.LeftShift)) upDown += 1f;
        if (Input.GetKey(KeyCode.LeftControl)) upDown -= 1f;

        Quaternion viewRot = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation;
        Vector3 forward = viewRot * Vector3.forward;
        Vector3 right = viewRot * Vector3.right;

        forward.y = 0f;
        forward.Normalize();
        right.y = 0f;
        right.Normalize();

        Vector3 moveDir = (forward * v + right * h).normalized * flySpeed;
        moveDir.y = upDown * verticalSpeed;

        localPlayer.SetVelocity(moveDir);
    }
}