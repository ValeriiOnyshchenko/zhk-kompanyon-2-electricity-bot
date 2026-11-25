using System.Text.RegularExpressions;

namespace ZhkElectricityBot.Stations;

/// <summary>One snapshot of a station: station summary + inverter details.</summary>
public sealed partial class StationReading
{
    public double? Soc { get; init; }
    public double? LoadW { get; init; }
    public double? BatteryW { get; init; }  // > 0 discharging
    public double? GridW { get; init; }
    public IReadOnlyList<double> GridVoltages { get; init; } = [];
    public double? CapacityKwh { get; init; }
    public DateTimeOffset? UpdatedUtc { get; init; }

    /// <summary>true = city power, false = running on batteries, null = can't tell.</summary>
    public bool? GridOn { get; init; }

    /// <summary>
    /// Grid presence: every grid phase voltage of the inverter above the threshold
    /// (GridVoltageL1/L2/L3 on three-phase, GridVoltageL1L2 on single-phase inverters).
    /// Without voltages: power drawn from the grid, or the battery carrying the load.
    /// </summary>
    public static StationReading From(StationLatest s, IReadOnlyList<DeviceData> inverters, BatteryOptions o, double? capacityOverride)
    {
        var voltages = inverters
            .SelectMany(d => d.Points.Keys.Where(k => GridVoltageKey().IsMatch(k)).Select(d.Number))
            .Where(v => v is not null).Select(v => v!.Value).ToList();

        bool? gridOn = voltages.Count > 0
            ? voltages.All(v => v >= o.GridVoltageThreshold)
            : s.WirePower is > 30 ? true
            : s.WirePower is not null && s.BatteryPower is > 30 ? false
            : null;

        var inverter = inverters.FirstOrDefault();
        return new StationReading
        {
            Soc = s.BatterySoc ?? inverter?.Number("SOC"),
            LoadW = s.ConsumptionPower ?? inverter?.Number("TotalConsumptionPower"),
            BatteryW = s.BatteryPower ?? inverter?.Number("BatteryPower"),
            GridW = s.WirePower ?? inverter?.Number("TotalGridPower"),
            GridVoltages = voltages,
            GridOn = gridOn,
            CapacityKwh = capacityOverride is > 0 ? capacityOverride : Capacity(inverters, o),
            UpdatedUtc = s.LastUpdate,
        };
    }

    /// <summary>
    /// Rated Ah × nominal voltage. The nominal voltage is derived from the measured one:
    /// LiFePO4 cells come in modules of 16 → 16 cells = 51.2 V, 96 cells (6 modules) = 307.2 V.
    /// </summary>
    private static double? Capacity(IReadOnlyList<DeviceData> inverters, BatteryOptions o)
    {
        foreach (var d in inverters)
        {
            var ah = d.Number("BatteryRatedCapacity");
            var volts = d.Number("BMSVoltage") is > 10 and var b ? b : d.Number("BatteryVoltage");
            if (ah is not > 0 || volts is not > 10) continue;
            var cells = Math.Max(1, Math.Round(volts.Value / 3.3 / 16)) * 16;
            return ah.Value * cells * o.NominalCellVoltage / 1000;
        }
        return null;
    }

    [GeneratedRegex(@"^GridVoltage(L\d|L1L2)$", RegexOptions.IgnoreCase)]
    private static partial Regex GridVoltageKey();
}
