using FeudalSim.Sim.Content;

namespace FeudalSim.Sim.World;

/// <summary>
/// The M1 graybox camp (Landfall, 21 §9.5): fixed places and shared stocks the utility AI acts on. Stocks are floats
/// changed per step (deterministic on one build). Real buildings, items and stores replace this in M2–M3 (14, 15).
/// </summary>
public struct CampRecord
{
    public byte Active;
    public float Food, Firewood, FireFuelMin, Threat;   // food in Satiety points (canon §10.9: 1 ration = 100)
    public float Bedding;                               // sleep factor (11 §3.2): bough bed 0.85
    public ushort Schedule;                             // schedule handle
    public float FireX, FireZ, WaterX, WaterZ, StoresX, StoresZ, ShelterX, ShelterZ, WoodsX, WoodsZ, ForageX, ForageZ;

    // 11 §13 / §9 exposure (M2-05a): the camp's shelter, the bed's ground insulation, where the camp lies, what settlers wear.
    public float ShelterWindBlock, ShelterRainBlock, ShelterInsulation, BeddingInsulation, ElevationM;
    public byte Coastal;
    public Worn Kit;

    public readonly (float X, float Z) Place(PlaceKind kind) => kind switch
    {
        PlaceKind.Fire => (FireX, FireZ),
        PlaceKind.Water => (WaterX, WaterZ),
        PlaceKind.Stores => (StoresX, StoresZ),
        PlaceKind.Shelter => (ShelterX, ShelterZ),
        PlaceKind.Woods => (WoodsX, WoodsZ),
        PlaceKind.ForageGround => (ForageX, ForageZ),
        _ => (0f, 0f),
    };

    public readonly float Stock(string id) => id switch
    {
        "food" => Food,
        "firewood" => Firewood,
        "fire_fuel_min" => FireFuelMin,
        "threat" => Threat,
        _ => 0f,
    };

    public void AddStock(string id, float delta)
    {
        switch (id)
        {
            case "food": Food = MathF.Max(0f, Food + delta); break;
            case "firewood": Firewood = MathF.Max(0f, Firewood + delta); break;
            case "fire_fuel_min": FireFuelMin = MathF.Max(0f, FireFuelMin + delta); break;
            case "threat": Threat = MathF.Max(0f, Threat + delta); break;
        }
    }
}
