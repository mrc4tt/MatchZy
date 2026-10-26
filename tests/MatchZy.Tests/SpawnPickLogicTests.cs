using System.Numerics;
using MatchZy;

namespace MatchZy.Tests;

// Aiming cases adapted from upstream shobhit-pathak/MatchZy#433, plus the fork's marker rules:
// floor box to number as aim target, standing-on exclusion and the stand-in-box fallback.
public class SpawnPickLogicTests
{
    // Player standing at the world origin, eyes 64u up.
    private static readonly Vector3 Origin = Vector3.Zero;
    private static readonly Vector3 Eye = new(0, 0, 64);
    // Midway between the floor box (floor + 8) and the number (floor + 40).
    private const float MarkerMid = 24f;

    private static (Vector3 Spawn, float FloorZ) Marker(float x, float y, float floorZ = 0f, float spawnZ = 0f)
        => (new Vector3(x, y, spawnZ), floorZ);

    private static Vector3 AimAt(Vector3 target) => Vector3.Normalize(target - Eye);

    private static int Pick(Vector3 forward, params (Vector3 Spawn, float FloorZ)[] markers)
        => SpawnPickLogic.PickMarker(Origin, Eye, forward, markers);

    [Fact]
    public void ForwardMatchesSourceAngles()
    {
        AssertClose(new Vector3(1, 0, 0), SpawnPickLogic.Forward(0, 0));
        AssertClose(new Vector3(0, 1, 0), SpawnPickLogic.Forward(0, 90));
        AssertClose(new Vector3(0, 0, -1), SpawnPickLogic.Forward(90, 0)); // positive pitch looks down
    }

    [Fact]
    public void PicksMarkerAimedAt()
    {
        var a = Marker(0, 250);
        var b = Marker(200, 0);
        Assert.Equal(1, Pick(AimAt(new Vector3(200, 0, MarkerMid)), a, b));
        Assert.Equal(0, Pick(AimAt(new Vector3(0, 250, MarkerMid)), a, b));
    }

    [Fact]
    public void PicksMarkerWhenAimingAtFloorBox()
    {
        Assert.Equal(0, Pick(AimAt(new Vector3(200, 0, SpawnPickLogic.FloorLift)), Marker(200, 0)));
    }

    [Fact]
    public void AimTargetFollowsFloorNotSpawnZ()
    {
        // Spawn origin floats 20u above the floor the box was drawn on: aim is measured from the floor.
        var marker = Marker(200, 0, floorZ: -30f, spawnZ: -10f);
        Assert.Equal(0, Pick(AimAt(new Vector3(200, 0, -30f + MarkerMid)), marker));
    }

    [Fact]
    public void IgnoresMarkerBesideTheView()
    {
        Assert.Equal(-1, Pick(AimAt(new Vector3(250, 0, MarkerMid)), Marker(250, 60)));
    }

    [Fact]
    public void IgnoresMarkerBehind()
    {
        Assert.Equal(-1, Pick(AimAt(new Vector3(200, 0, MarkerMid)), Marker(-200, 0)));
    }

    [Fact]
    public void IgnoresMarkerWhenLookingOverIt()
    {
        Assert.Equal(-1, Pick(SpawnPickLogic.Forward(-30, 0), Marker(300, 0)));
    }

    [Fact]
    public void PicksNearestOfMarkersInLine()
    {
        Assert.Equal(1, Pick(AimAt(new Vector3(250, 0, MarkerMid)), Marker(250, 0), Marker(120, 0)));
    }

    [Fact]
    public void FarMarkerUsesSameToleranceAsNear()
    {
        // A fixed radius, not an angular cone: 2000u away, 20u off the line still hits, 40u does not.
        Vector3 forward = AimAt(new Vector3(2000, 0, MarkerMid));
        Assert.Equal(0, Pick(forward, Marker(2000, 20)));
        Assert.Equal(-1, Pick(forward, Marker(2000, 40)));
    }

    [Fact]
    public void IgnoresMarkerBeyondMaxDistance()
    {
        float x = SpawnPickLogic.AimMaxDistance + 200;
        Assert.Equal(-1, Pick(AimAt(new Vector3(x, 0, MarkerMid)), Marker(x, 0)));
    }

    [Fact]
    public void SpawnStoodOnIsNotAimable()
    {
        // Looking straight down onto the spawn you're on: no aim hit, falls back to the box you
        // stand in (same index), so USE re-centers rather than doing nothing.
        Assert.Equal(0, Pick(SpawnPickLogic.Forward(89, 0), Marker(0, 0)));
        // A neighbour in view still wins over the spawn under your feet.
        Assert.Equal(1, Pick(AimAt(new Vector3(150, 0, MarkerMid)), Marker(0, 0), Marker(150, 0)));
    }

    [Fact]
    public void StandingInBoxPicksItWhenNotAiming()
    {
        // 10u off center (outside the 8u standing-on radius, inside the 16u half box), looking at the sky.
        Assert.Equal(0, Pick(SpawnPickLogic.Forward(-89, 0), Marker(10, 0)));
    }

    [Fact]
    public void AimBeatsStandingInBox()
    {
        // Box center behind and beside the feet, so its own vertical line is out of the view ray.
        Assert.Equal(1, Pick(AimAt(new Vector3(200, 0, MarkerMid)), Marker(-6, -12), Marker(200, 0)));
    }

    [Fact]
    public void OverlappingBoxesPickNearestCenter()
    {
        Vector3 sky = SpawnPickLogic.Forward(-89, 0);
        Assert.Equal(1, Pick(sky, Marker(-14, 0), Marker(10, 0)));
    }

    [Fact]
    public void StandingFarAboveBoxDoesNotPickIt()
    {
        Vector3 sky = SpawnPickLogic.Forward(-89, 0);
        Assert.Equal(-1, Pick(sky, Marker(10, 0, floorZ: -(SpawnPickLogic.StandInHeight + 1))));
        Assert.Equal(-1, Pick(sky, Marker(20, 0))); // outside the box horizontally
    }

    [Fact]
    public void NoMarkers()
    {
        Assert.Equal(-1, Pick(SpawnPickLogic.Forward(0, 0)));
    }

    [Fact]
    public void RayParallelToSegmentDoesNotThrow()
    {
        var (distance, along) = SpawnPickLogic.RayToVerticalSegment(
            new Vector3(0, 0, 100), new Vector3(0, 0, -1), 4096, 0, 0, 8, 40);
        Assert.Equal(0f, distance, 3);
        Assert.Equal(60f, along, 3);
    }

    private static void AssertClose(Vector3 expected, Vector3 actual)
    {
        Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"expected {expected}, got {actual}");
    }
}
