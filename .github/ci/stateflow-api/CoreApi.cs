using CoCoFlow.Runtime.Core;

namespace CoCoFlow.CI
{
    public static class CoreApi
    {
        public static T GetService<T>() where T : class => CoCoServices.Get<T>();

        public static CoCoDiagnostic CheckService(object service)
        {
            using (CoCoServices.WaitFor<object>(ready => CoCoServices.Unregister(ready)))
                CoCoServices.Register(service);
            CoCoServices.TryGet<object>(out _);
            CoCoServices.Unregister(service);
            return CoCoDiagnostic.Error(CoCoDiagnosticDomain.Registry,
                CoCoDiagnosticCode.ManifestConflict, "Missing service");
        }
    }
}
