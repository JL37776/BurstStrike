using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.Map
{
    /// <summary>
    /// Scene-view 风格的 3D 摄像机控制器（参考 Unity Editor Scene 视图）。
    /// <list type="bullet">
    ///   <item>鼠标右键拖拽 → 旋转视角（FPS 风格）</item>
    ///   <item>鼠标中键拖拽 → 平移</item>
    ///   <item>滚轮 → 缩放（前后移动）</item>
    ///   <item>右键按住 + WASD → 飞行漫游</item>
    ///   <item>右键按住 + QE → 上下飞行</item>
    ///   <item>Shift 加速</item>
    ///   <item>F 键 → 聚焦原点</item>
    /// </list>
    /// </summary>
    public sealed class SceneViewCamera : MonoBehaviour
    {
        [Header("旋转")]
        [SerializeField] private float rotateSpeed = 3f;

        [Header("平移")]
        [SerializeField] private float panSpeed = 0.5f;

        [Header("缩放")]
        [SerializeField] private float zoomSpeed = 10f;
        [SerializeField] private float zoomSmooth = 8f;

        [Header("飞行漫游 (右键+WASD)")]
        [SerializeField] private float moveSpeed = 15f;
        [SerializeField] private float shiftMultiplier = 3f;

        [Header("限制")]
        [SerializeField] private float minY = 1f;
        [SerializeField] private float maxY = 500f;

        private float _yaw;
        private float _pitch;
        private float _targetZoomDelta;

        /// <summary>外部注入的归位回调，按 F 键时调用</summary>
        public System.Action OnFocusRequest { get; set; }

        /// <summary>外部同步旋转状态（例如摄像机被编辑器重新定位后）</summary>
        public void SetRotation(float pitch, float yaw)
        {
            _pitch = Mathf.Clamp(pitch, -89f, 89f);
            _yaw = yaw;
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0);
        }

        private void Start()
        {
            var euler = transform.eulerAngles;
            _yaw = euler.y;
            _pitch = euler.x;
        }

        private void Update()
        {
            bool rmb = Input.GetMouseButton(1);
            bool mmb = Input.GetMouseButton(2);

            // ── 旋转（右键拖拽）──
            if (rmb)
            {
                float dx = Input.GetAxis("Mouse X");
                float dy = Input.GetAxis("Mouse Y");
                _yaw += dx * rotateSpeed;
                _pitch -= dy * rotateSpeed;
                _pitch = Mathf.Clamp(_pitch, -89f, 89f);
                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0);
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            // ── 平移（中键拖拽）──
            if (mmb)
            {
                float dx = -Input.GetAxis("Mouse X") * panSpeed;
                float dy = -Input.GetAxis("Mouse Y") * panSpeed;
                transform.Translate(dx, dy, 0, Space.Self);
            }
            if(!(EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()))
            {
                // ── 缩放（滚轮）──
                float scroll = Input.GetAxis("Mouse ScrollWheel");
                if (Mathf.Abs(scroll) > 0.001f)
                {
                    _targetZoomDelta += scroll * zoomSpeed;
                }

                if (Mathf.Abs(_targetZoomDelta) > 0.01f)
                {
                    float step = _targetZoomDelta * Time.unscaledDeltaTime * zoomSmooth;
                    transform.Translate(0, 0, step, Space.Self);
                    _targetZoomDelta -= step;
                }
            }

            // ── 飞行漫游（右键 + WASD/QE）──
            if (rmb)
            {
                float speed = moveSpeed;
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                    speed *= shiftMultiplier;

                var move = Vector3.zero;
                if (Input.GetKey(KeyCode.W)) move += Vector3.forward;
                if (Input.GetKey(KeyCode.S)) move += Vector3.back;
                if (Input.GetKey(KeyCode.A)) move += Vector3.left;
                if (Input.GetKey(KeyCode.D)) move += Vector3.right;
                if (Input.GetKey(KeyCode.E)) move += Vector3.up;
                if (Input.GetKey(KeyCode.Q)) move += Vector3.down;

                transform.Translate(move * (speed * Time.unscaledDeltaTime), Space.Self);
            }

            // ── F 键归位 ──
            if (Input.GetKeyDown(KeyCode.F) && !rmb)
            {
                OnFocusRequest?.Invoke();
            }

            // ── 高度限制 ──
            var pos = transform.position;
            pos.y = Mathf.Clamp(pos.y, minY, maxY);
            transform.position = pos;
        }
    }
}
