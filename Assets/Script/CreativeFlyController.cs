using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class CreativeFlyController : UdonSharpBehaviour
{
    [Tooltip("通常時の移動速度")]
    public float moveSpeed = 3.0f;

    [Tooltip("ダッシュ時の移動速度")]
    public float sprintSpeed = 6.0f;

    private VRCPlayerApi localPlayer;
    private bool isFlying = false; // フライモードが有効かどうかのフラグ

    void Start()
    {
        localPlayer = Networking.LocalPlayer;
    }
    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        if (player.isLocal)
        {
            EnableFly();
        }
    }

    public void EnableFly()
    {
        isFlying = true;
        localPlayer.SetGravityStrength(0f);
    }

    // フライモードを無効にする関数
    public void DisableFly()
    {
        isFlying = false;
        localPlayer.SetGravityStrength(1.0f);
        localPlayer.SetVelocity(Vector3.zero);
    }

    void Update()
    {
        // フライモードが有効でなければ、以下の処理は行わない
        if (!isFlying)
        {
            return;
        }

        // --- キーボード入力 ---
        // WASDキーで前後左右の入力を取得（-1.0f ～ 1.0fの値）
        float horizontalInput = Input.GetAxis("Horizontal"); // A, Dキー
        float verticalInput = Input.GetAxis("Vertical");     // W, Sキー

        // 上昇（スペースキー）と下降（左Shiftキー）の判定
        bool isMovingUp = Input.GetKey(KeyCode.LeftShift);
        bool isMovingDown = Input.GetKey(KeyCode.LeftControl);

        // ダッシュ（左Controlキー）の判定
        bool isSprinting = Input.GetKey(KeyCode.Space);

        float currentSpeed = isSprinting ? sprintSpeed : moveSpeed;
        Quaternion headRotation = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation;

        Vector3 horizontalVelocity = headRotation * new Vector3(horizontalInput, 0, verticalInput) * currentSpeed;

        float verticalVelocityY = 0;
        if (isMovingUp) verticalVelocityY = currentSpeed;
        if (isMovingDown) verticalVelocityY = -currentSpeed;

        Vector3 finalVelocity = new Vector3(horizontalVelocity.x, verticalVelocityY, horizontalVelocity.z);

        localPlayer.SetVelocity(finalVelocity);
    }
}
