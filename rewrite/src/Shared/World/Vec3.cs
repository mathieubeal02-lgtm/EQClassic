namespace EQClassic.Shared.World;

/// <summary>A position in server (EverQuest) coordinates: X and Y on the ground plane, Z up.</summary>
public readonly record struct Vec3(float X, float Y, float Z);
