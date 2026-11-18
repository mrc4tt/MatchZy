using System.Numerics;
using MatchZy;

namespace MatchZy.Tests;

public class GrenadeSimLogicTests
{
    // Flat floor at z = 0, nothing else.
    private static bool Floor(Vector3 from, Vector3 to, out Vector3 hit, out Vector3 normal, out float fraction)
    {
        hit = default; normal = default; fraction = 1f;
        if (to.Z > 0f || from.Z < 0f) return false;
        fraction = from.Z / (from.Z - to.Z);
        hit = from + (to - from) * fraction;
        normal = Vector3.UnitZ;
        return true;
    }

    [Fact]
    public void StandingLaunchMatchesEngineThrow()
    {
        // Real standing throw from step 0 (.calib CSV, de_dust2): feet, view offset, eye angles
        // and the engine's launch position and velocity.
        var eye = new Vector3(-476.80328f, -228.16489f, 0.0761013f + 63.859375f);
        var l = GrenadeSimLogic.LaunchFrom(eye, -24.815992f, 88.68193f, 1f, Vector3.Zero);
        Assert.InRange(Vector3.Distance(l.Position, new Vector3(-476.49136f, -214.6084f, 72.42807f)), 0f, 0.05f);
        Assert.InRange(Vector3.Distance(l.Velocity, new Vector3(13.158993f, 571.9146f, 358.28134f)), 0f, 0.1f);
    }

    [Fact]
    public void GrenadeComesToRestOnFlatFloor()
    {
        var l = GrenadeSimLogic.LaunchFrom(new Vector3(0, 0, 0), GrenadeSimLogic.ThrowKind.Stand, -30f, 0f);
        var f = GrenadeSimLogic.Simulate(l, Floor);
        Assert.True(f.Stopped);
        Assert.True(f.Bounces >= 1);
        Assert.InRange(f.Rest.Z, -0.01f, 0.01f);
        Assert.True(f.Rest.X > f.FirstImpact.X - 0.01f); // it rolls/bounces forward, not back
    }

    [Fact]
    public void BallisticPitchesReachTheTarget()
    {
        var feet = new Vector3(0, 0, 0);
        var target = new Vector3(900, 300, 0);
        var pitches = GrenadeSimLogic.BallisticPitches(feet, GrenadeSimLogic.ThrowKind.Stand, target);
        Assert.NotEmpty(pitches);
        float yaw = GrenadeSimLogic.YawTo(feet, target);
        foreach (var p in pitches)
        {
            var f = GrenadeSimLogic.Simulate(GrenadeSimLogic.LaunchFrom(feet, GrenadeSimLogic.ThrowKind.Stand, p, yaw), Floor);
            // First floor contact is the target (the bounce afterwards moves it on).
            Assert.InRange(GrenadeSimLogic.HorizontalDistance(f.FirstImpact, target), 0f, 3f);
        }
    }

    [Fact]
    public void JumpthrowReachesFurtherThanStanding()
    {
        float stand = GrenadeSimLogic.ReachAtHeight(Vector3.Zero, GrenadeSimLogic.ThrowKind.Stand, -30f, 0f, 0f);
        float jump = GrenadeSimLogic.ReachAtHeight(Vector3.Zero, GrenadeSimLogic.ThrowKind.Jump, -30f, 0f, 0f);
        Assert.True(jump > stand);
    }

    [Fact]
    public void RunningJumpthrowStartsAheadAndReachesFurthest()
    {
        var l = GrenadeSimLogic.LaunchFrom(Vector3.Zero, GrenadeSimLogic.ThrowKind.RunJump, -30f, 90f);
        // Released past the jump point (run-up + the distance covered before the release), along the yaw.
        Assert.InRange(l.Position.X, -0.01f, 0.01f);
        Assert.True(l.Position.Y > GrenadeSimLogic.RunUp + 25f);
        float jump = GrenadeSimLogic.ReachAtHeight(Vector3.Zero, GrenadeSimLogic.ThrowKind.Jump, -30f, 0f, 0f);
        float run = GrenadeSimLogic.ReachAtHeight(Vector3.Zero, GrenadeSimLogic.ThrowKind.RunJump, -30f, 0f, 0f);
        Assert.True(run > jump + 300f);
    }

    [Fact]
    public void PolygonContainmentAndCentroid()
    {
        var square = new List<Vector3> { new(0, 0, 10), new(100, 0, 10), new(100, 100, 10), new(0, 100, 10) };
        Assert.True(GrenadeSimLogic.InsidePolygon(new Vector3(50, 50, 0), square));
        Assert.False(GrenadeSimLogic.InsidePolygon(new Vector3(150, 50, 0), square));
        var c = GrenadeSimLogic.Centroid(square);
        Assert.InRange(c.X, 49.9f, 50.1f);
        Assert.InRange(c.Y, 49.9f, 50.1f);
        Assert.InRange(c.Z, 9.9f, 10.1f);
    }
}
