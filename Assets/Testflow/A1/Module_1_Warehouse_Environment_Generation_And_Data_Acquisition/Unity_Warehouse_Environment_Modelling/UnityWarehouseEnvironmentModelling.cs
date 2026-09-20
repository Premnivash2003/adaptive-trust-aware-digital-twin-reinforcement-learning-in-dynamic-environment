using UnityEngine;
using ATADTRL.Environment;

namespace ATADTRL.TestFlow.A1.Module1
{
    /// <summary>
    /// Architecture adapter for "Unity Warehouse Environment Modelling".
    /// It exposes the shared data-driven WarehouseManager as the A1 modelling
    /// submodule without duplicating warehouse geometry or scene references.
    /// </summary>
    public static class UnityWarehouseEnvironmentModelling
    {
        public static WarehouseManager ResolveModel()
        {
            return Object.FindAnyObjectByType<WarehouseManager>();
        }

        public static bool IsModelReady(WarehouseManager warehouse)
        {
            return warehouse != null && warehouse.RackRoot != null &&
                   warehouse.Inventory != null && warehouse.AisleBounds != null &&
                   warehouse.AisleBounds.Count > 0;
        }

        public static void BuildModel(WarehouseManager warehouse)
        {
            if (warehouse == null)
            {
                Debug.LogError("ATADTRL Module 1: WarehouseManager is missing; warehouse model cannot be built.");
                return;
            }
            warehouse.BuildWarehouse();
        }
    }
}
