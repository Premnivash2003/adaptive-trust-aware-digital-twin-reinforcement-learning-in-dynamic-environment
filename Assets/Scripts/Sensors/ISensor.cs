namespace ATADTRL.Sensors
{
    /// <summary>
    /// Common contract for all simulated sensors. Ground truth and the
    /// sensor's final (possibly noisy/dropped-out) observation are kept
    /// explicitly separate: TickSensor() reads ground truth internally,
    /// but only the noisy public getters should be used to build
    /// Unified_Observation.csv.
    /// </summary>
    public interface ISensor
    {
        string SensorName { get; }
        float UpdateRateHz { get; }
        bool LastReadingDroppedOut { get; }
        void TickSensor(float deltaTime);
    }
}
