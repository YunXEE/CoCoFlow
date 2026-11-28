using System;

// Intent 与 Operation 的绑定沿用 Catalog 对项目类型的同一种标识计算。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>从项目类型取得稳定的绑定标识片段。</summary>
    internal static class HostBindingIds
    {
        internal static ulong Hash(Type type)
        {
            ulong hash = GraphCatalogHash.OffsetBasis;
            GraphCatalogHash.Add(ref hash, type);
            return hash;
        }
    }
}
