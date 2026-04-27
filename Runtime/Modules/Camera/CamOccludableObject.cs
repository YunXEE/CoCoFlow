using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Camera
{
    [RequireComponent(typeof(Renderer))]
    public class OccludableObject : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("透明化的速度")]
        [SerializeField] private float fadeSpeed = 3f;
        [Tooltip("最大透明度（0为完全透明，1为完全不透明）")]
        [SerializeField] private float targetDitherThreshold = 0.2f;

        private Renderer _meshRenderer;
        private MaterialPropertyBlock _propBlock;
        
        // 缓存 Shader 属性 ID，提升性能
        private static readonly int DitherThresholdID = Shader.PropertyToID("_DitherThreshold");

        private float _currentThreshold = 1f;
        private float _targetThreshold = 1f;
        private bool _isFading = false;

        private void Awake()
        {
            _meshRenderer = GetComponent<Renderer>();
            _propBlock = new MaterialPropertyBlock();
            
            // 初始化为不透明
            _meshRenderer.GetPropertyBlock(_propBlock);
            _propBlock.SetFloat(DitherThresholdID, 1f);
            _meshRenderer.SetPropertyBlock(_propBlock);
        }

        public void FadeOut()
        {
            _targetThreshold = targetDitherThreshold;
            _isFading = true;
        }

        public void FadeIn()
        {
            _targetThreshold = 1f;
            _isFading = true;
        }

        private void Update()
        {
            if (!_isFading) return;

            // 平滑过渡
            _currentThreshold = Mathf.MoveTowards(_currentThreshold, _targetThreshold, fadeSpeed * Time.deltaTime);
            
            // 应用到 MaterialPropertyBlock
            _meshRenderer.GetPropertyBlock(_propBlock);
            _propBlock.SetFloat(DitherThresholdID, _currentThreshold);
            _meshRenderer.SetPropertyBlock(_propBlock);

            // 如果到达目标值，停止 Update 逻辑
            if (Mathf.Approximately(_currentThreshold, _targetThreshold))
            {
                _isFading = false;
            }
        }
    }
}