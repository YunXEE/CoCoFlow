using System;
using CoCoFlow.Runtime.Content;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.UI
{
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    internal sealed class UIPanelSourceOwnership : MonoBehaviour
    {
        private ContentScope _sourceScope;
        private ContentLease<GameObject> _sourceLease;
        private bool _isBound;

        internal void Bind(
            ContentScope sourceScope,
            ContentLease<GameObject> sourceLease)
        {
            if (_isBound)
            {
                throw new InvalidOperationException("Panel source ownership is already bound.");
            }

            _sourceScope = sourceScope ?? throw new ArgumentNullException(nameof(sourceScope));
            _sourceLease = sourceLease ?? throw new ArgumentNullException(nameof(sourceLease));
            _isBound = true;
        }

        private void OnDestroy()
        {
            _sourceLease?.Dispose();
            _sourceLease = null;
            _sourceScope?.Dispose();
            _sourceScope = null;
            _isBound = false;
        }
    }
}
