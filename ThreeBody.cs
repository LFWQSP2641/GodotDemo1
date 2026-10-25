using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;

public partial class ThreeBody : Node2D
{
    [Export] public int NumberOfBodies = 16;
    [Export] public float GravitationalConstant = 10000.0f;
    [Export] public float SofteningFactor = 4000.0f;
    [Export] public bool EnablePullBack = true;
    [Export] public float PullBackThreshold = 1000.0f;

    private Body[] _bodies;
    private SimulationFramePacket[] _latestFramePackets;

    private readonly ConcurrentQueue<(SimulationFramePacket[], SimulationFramePacket[])> _framePackets = new();

    // private bool _paused;
    private CancellationTokenSource _cancellationTokenSource;
    private Task _simulationTask;

    private double _interpolationTimer;

    private const double ProducerStep = 1.0 / 60.0;
    private const int SubSteps = 1;

    public override void _Ready()
    {
        ResetSimulation();
    }

    public override void _Process(double delta)
    {
        if (_cancellationTokenSource == null
            || _cancellationTokenSource.IsCancellationRequested)
        {
            return;
        }

#if false
        if (_framePackets.TryDequeue(out var newFramePackets))
        {
            for (var i = 0; i < _bodies.Length; i++)
            {
                var body = _bodies[i];
                var packet = newFramePackets[i];
                body.Position = packet.Position;
                body.Velocity = packet.Velocity;
                body.Acceleration = packet.Acceleration;

                body.UpdateTrail();
            }
        }
#else
        _interpolationTimer += delta;
        if (_interpolationTimer < ProducerStep)
        {
            if (_framePackets.TryPeek(out var framePacketsPair))
            {
                var previousFramePackets = framePacketsPair.Item1;
                var targetFramePackets = framePacketsPair.Item2;
                var tau = Mathf.Clamp(
                    (float)(_interpolationTimer / ProducerStep),
                    0.0f,
                    1.0f);

                var interpolationStep = (float)ProducerStep;

                for (var i = 0; i < _bodies.Length; i++)
                {
                    var body = _bodies[i];
                    var oldPacket = previousFramePackets[i];
                    var newPacket = targetFramePackets[i];

                    body.Position = CubicHermite(
                        oldPacket.Position,
                        oldPacket.Velocity,
                        newPacket.Position,
                        newPacket.Velocity,
                        tau,
                        interpolationStep);

                    body.Velocity = CubicHermite(
                        oldPacket.Velocity,
                        oldPacket.Acceleration,
                        newPacket.Velocity,
                        newPacket.Acceleration,
                        tau,
                        interpolationStep);

                    body.Acceleration = oldPacket.Acceleration.Lerp(
                        newPacket.Acceleration,
                        tau);
                    
                    body.UpdateTrail();
                }
            }
        }
        else
        {
            if (_framePackets.TryDequeue(out var framePacketsPair))
            {
                _interpolationTimer -= ProducerStep;
                var targetFramePackets = framePacketsPair.Item2;
                for (var i = 0; i < _bodies.Length; i++)
                {
                    var body = _bodies[i];
                    var packet = targetFramePackets[i];
                    body.Position = packet.Position;
                    body.Velocity = packet.Velocity;
                    body.Acceleration = packet.Acceleration;

                    body.UpdateTrail();
                }
            }
        }
#endif
        
        if (_cancellationTokenSource is { IsCancellationRequested: false }
            && (_simulationTask == null || _simulationTask.IsCompleted))
        {
            var cancellationToken = _cancellationTokenSource.Token;
            _simulationTask = Task.Run(
                () => StartSimulation(cancellationToken),
                cancellationToken);
        }
    }

    public void PauseSimulation()
    {
        var cancellationTokenSource = _cancellationTokenSource;
        cancellationTokenSource?.Cancel();
        try
        {
            _simulationTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _simulationTask = null;
            cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    public void ResetSimulation()
    {
        PauseSimulation();
        while (_framePackets.TryDequeue(out _))
        {
        }

        foreach (var body in _bodies ?? [])
        {
            body.QueueFree();
        }
        _bodies = new Body[NumberOfBodies];
        RandomNumberGenerator rng = new();
        for (var i = 0; i < NumberOfBodies; i++)
        {
            var body = new Body();
            body.AddToGroup("bodies");
            var angle = rng.RandfRange(0f, Mathf.Pi * 2.0f);
            var radius = rng.RandfRange(300.0f, 600.0f);
            body.Position = new Vector2(800, 600) + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            body.Velocity = new Vector2(rng.RandfRange(-200, 200), rng.RandfRange(-200, 200));
            body.Mass = rng.RandfRange(500.0f, 2000.0f);
            _bodies[i] = body;
            AddChild(body);
        }

        var totalMomentum = Vector2.Zero;
        var totalMass = 0.0f;

        foreach (var body in _bodies)
        {
            totalMomentum += body.Velocity * body.Mass;
            totalMass += body.Mass;
        }

        var centerOfMassVelocity = totalMomentum / totalMass;

        foreach (var body in _bodies)
        {
            body.Velocity -= centerOfMassVelocity;
        }

        foreach (var body in _bodies)
        {
            if (body != null)
            {
                body.Acceleration = Vector2.Zero;
            }
        }

        var bodyCount = _bodies.Length;
        for (var i = 0; i < bodyCount; i++)
        {
            var bodyA = _bodies[i];
            for (var j = i + 1; j < bodyCount; j++)
            {
                var bodyB = _bodies[j];
                if (bodyA != null && bodyB != null)
                {
                    var direction = bodyB.Position - bodyA.Position;
                    var distanceSquared = direction.LengthSquared() + SofteningFactor;
                    var forceMagnitude = GravitationalConstant * bodyA.Mass * bodyB.Mass / distanceSquared;
                    var force = direction.Normalized() * forceMagnitude;

                    bodyA.Acceleration += force / bodyA.Mass;
                    bodyB.Acceleration -= force / bodyB.Mass;
                }
            }
        }

        _latestFramePackets = new SimulationFramePacket[_bodies.Length];
        for (var i = 0; i < _bodies.Length; i++)
        {
            var body = _bodies[i];
            _latestFramePackets[i] = new SimulationFramePacket
            {
                Position = body.Position,
                Velocity = body.Velocity,
                Acceleration = body.Acceleration,
                Mass = body.Mass,
            };
        }

        _cancellationTokenSource = new CancellationTokenSource();
    }

    private void StartSimulation(CancellationToken cancellationToken)
    {
        const float dt = (float)(ProducerStep / SubSteps);
        var newFramePackets = _latestFramePackets;

        var step = 0;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            newFramePackets = SimulationFramePackets(newFramePackets, dt);
            step++;
        } while (step < SubSteps);

        cancellationToken.ThrowIfCancellationRequested();

        _framePackets.Enqueue((_latestFramePackets, newFramePackets));

        _latestFramePackets = newFramePackets;
    }

    private SimulationFramePacket[] SimulationFramePackets(SimulationFramePacket[] oldFramePackets, float dt)
    {
        var n = oldFramePackets.Length;
        var newFramePackets = new SimulationFramePacket[n];

        Parallel.For(0, n, i =>
        {
            var oldPacket = oldFramePackets[i];
            var oldAcc = oldPacket.Acceleration;
            var newPosition = oldPacket.Position + oldPacket.Velocity * dt + 0.5f * oldAcc * (dt * dt);

            newFramePackets[i] = new SimulationFramePacket
            {
                Position = newPosition,
                Velocity = Vector2.Zero, // Placeholder for velocity calculation
                Acceleration = Vector2.Zero, // Placeholder for acceleration calculation
                Mass = oldPacket.Mass,
            };
        });

        Parallel.For(0, n, i =>
        {
            var oldPacket = oldFramePackets[i];
            var newPacket = newFramePackets[i];
            var newAcc = Vector2.Zero;
            for (var j = 0; j < n; j++)
            {
                if (i == j)
                {
                    continue;
                }
                var otherPacket = oldFramePackets[j];
                var direction = otherPacket.Position - newPacket.Position;
                var distanceSquared = direction.LengthSquared() + SofteningFactor;
                var forceMagnitude = GravitationalConstant * oldPacket.Mass * otherPacket.Mass / distanceSquared;
                var force = direction.Normalized() * forceMagnitude;

                newAcc += force / oldPacket.Mass;
            }

            var newVelocity = oldPacket.Velocity + 0.5f * (oldPacket.Acceleration + newAcc) * dt;

            newFramePackets[i] = newPacket with
            {
                Velocity = newVelocity,
                Acceleration = newAcc,
            };
        });

        if (EnablePullBack)
        {
            var bodiesCenter =
                newFramePackets.Aggregate(Vector2.Zero, (current, packet) => current + packet.Position);
            bodiesCenter /= _bodies.Length;

            Parallel.For(0, n, i =>
            {
                var newPacket = newFramePackets[i];
                var directionToCenter = bodiesCenter - newPacket.Position;
                var distanceToCenter = directionToCenter.Length();
                if (!(distanceToCenter > PullBackThreshold))
                {
                    return;
                }
                var pullBackForce = directionToCenter * 0.1f;
                var newVelocity = newPacket.Velocity + pullBackForce * dt;

                newFramePackets[i] = newPacket with
                {
                    Velocity = newVelocity,
                };
            });
        }

        return newFramePackets;
    }

    private static Vector2 CubicHermite(Vector2 p0, Vector2 v0, Vector2 p1, Vector2 v1, float tau, float dt)
    {
        var tau2 = tau * tau;
        var tau3 = tau2 * tau;

        var h00 = 2.0f * tau3 - 3.0f * tau2 + 1.0f;
        var h10 = tau3 - 2.0f * tau2 + tau;
        var h01 = -2.0f * tau3 + 3.0f * tau2;
        var h11 = tau3 - tau2;

        var m0 = v0 * dt;
        var m1 = v1 * dt;

        return h00 * p0 + h10 * m0 + h01 * p1 + h11 * m1;
    }

    private record SimulationFramePacket
    {
        // public long FrameNumber { get; init; }
        public Vector2 Position { get; init; }
        public Vector2 Velocity { get; init; }
        public Vector2 Acceleration { get; init; }
        public float Mass { get; init; }
    }
}
