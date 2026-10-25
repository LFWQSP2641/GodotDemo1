using System;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Godot;

public partial class ThreeBody : Node2D
{
    [Export] public int NumberOfBodies = 16;
    [Export] public float GravitationalConstant = 10000.0f;
    [Export] public float SofteningFactor = 4000.0f;
    [Export] public bool EnablePullBack = true;
    [Export] public float PullBackThreshold = 1000.0f;
    [Export] public bool EnableInterpolation = true;

    public event Action? FrameSkipped;

    private Body[]? _bodies;
    private SimulationFramePacket[]? _latestFramePackets;

    private (SimulationFramePacket[], SimulationFramePacket[]) _currentFramePacketsPair;
    private readonly Channel<(SimulationFramePacket[], SimulationFramePacket[])> _framePacketsChannel =
        Channel.CreateBounded<(SimulationFramePacket[], SimulationFramePacket[])>(new BoundedChannelOptions(16)
    {
        SingleReader = true,
        SingleWriter = true,
        FullMode = BoundedChannelFullMode.Wait,
    });

    // private bool _paused;
    private CancellationTokenSource? _cancellationTokenSource;

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

        if (!EnableInterpolation)
        {
            _interpolationTimer += delta;
            if (_interpolationTimer >= ProducerStep
                || _currentFramePacketsPair == default)
            {
                _interpolationTimer -= ProducerStep;
                var success = _framePacketsChannel.Reader.TryRead(out var newFramePacketsPair);
                if (!success)
                {
                    Task.Run(() => FrameSkipped?.Invoke());
                    return;
                }
                _currentFramePacketsPair = newFramePacketsPair;
            }
            if (_currentFramePacketsPair == default)
            {
                return;
            }
            var targetFramePackets = _currentFramePacketsPair.Item2;
            for (var i = 0; i < _bodies!.Length; i++)
            {
                var body = _bodies[i];
                var packet = targetFramePackets[i];
                body.Position = packet.Position;
                body.Velocity = packet.Velocity;
                body.Acceleration = packet.Acceleration;

                body.UpdateTrail();
            }
        }
        else
        {
            _interpolationTimer += delta;
            while (_interpolationTimer >= ProducerStep
                || _currentFramePacketsPair == default)
            {
                _interpolationTimer -= ProducerStep;
                var success = _framePacketsChannel.Reader.TryRead(out var newFramePacketsPair);
                if (!success)
                {
                    Task.Run(() => FrameSkipped?.Invoke());
                    return;
                }
                _currentFramePacketsPair = newFramePacketsPair;
            }
            if (_currentFramePacketsPair == default)
            {
                return;
            }
            var previousFramePackets = _currentFramePacketsPair.Item1;
            var targetFramePackets = _currentFramePacketsPair.Item2;
            var tau = Mathf.Clamp(
                (float)(_interpolationTimer / ProducerStep),
                0.0f,
                1.0f);

            const float interpolationStep = (float)ProducerStep;

            // for (var i = 0; i < (_bodies?.Length ?? 0); i++)
            // {
            //     var body = _bodies![i];
            //     var oldPacket = previousFramePackets[i];
            //     var newPacket = targetFramePackets[i];

            //     body.Position = CubicHermite(
            //         oldPacket.Position,
            //         oldPacket.Velocity,
            //         newPacket.Position,
            //         newPacket.Velocity,
            //         tau,
            //         interpolationStep);

            //     body.Velocity = CubicHermite(
            //         oldPacket.Velocity,
            //         oldPacket.Acceleration,
            //         newPacket.Velocity,
            //         newPacket.Acceleration,
            //         tau,
            //         interpolationStep);

            //     body.Acceleration = oldPacket.Acceleration.Lerp(
            //         newPacket.Acceleration,
            //         tau);

            //     body.UpdateTrail();
            // }
            var currentFramePackets = new SimulationFramePacket[_bodies!.Length];
            Parallel.For(0, _bodies.Length, i =>
            {
                var oldPacket = previousFramePackets[i];
                var newPacket = targetFramePackets[i];

                var interpolatedPosition = CubicHermite(
                    oldPacket.Position,
                    oldPacket.Velocity,
                    newPacket.Position,
                    newPacket.Velocity,
                    tau,
                    interpolationStep);

                var interpolatedVelocity = CubicHermite(
                    oldPacket.Velocity,
                    oldPacket.Acceleration,
                    newPacket.Velocity,
                    newPacket.Acceleration,
                    tau,
                    interpolationStep);

                // It has no impact.
                // var interpolatedAcceleration = oldPacket.Acceleration.Lerp(
                //     newPacket.Acceleration,
                //     tau);
                var interpolatedAcceleration = oldPacket.Acceleration;

                currentFramePackets[i] = new SimulationFramePacket
                {
                    Position = interpolatedPosition,
                    Velocity = interpolatedVelocity,
                    Acceleration = interpolatedAcceleration,
                    Mass = oldPacket.Mass,
                };
            });
            for (var i = 0; i < _bodies!.Length; i++)
            {
                var body = _bodies[i];
                var packet = currentFramePackets[i];
                body.Position = packet.Position;
                body.Velocity = packet.Velocity;
                body.Acceleration = packet.Acceleration;

                body.UpdateTrail();
            }
        }
    }

    public void PauseSimulation()
    {
        var cancellationTokenSource = _cancellationTokenSource;
        cancellationTokenSource?.Cancel();
        cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }

    public void ResetSimulation()
    {
        PauseSimulation();
        while (_framePacketsChannel.Reader.TryRead(out _))
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
            body.Acceleration = Vector2.Zero;
        }

        var bodyCount = _bodies.Length;
        for (var i = 0; i < bodyCount; i++)
        {
            var bodyA = _bodies[i];

            for (var j = i + 1; j < bodyCount; j++)
            {
                var bodyB = _bodies[j];
                var direction = bodyB.Position - bodyA.Position;

                var distanceSquared = direction.LengthSquared() + SofteningFactor;
                var inverseDistance = (float)(1.0 / Math.Sqrt(distanceSquared));
                var force = direction * (
                    GravitationalConstant
                    * bodyA.Mass
                    * bodyB.Mass
                    * inverseDistance
                    * inverseDistance
                    * inverseDistance);

                bodyA.Acceleration += force / bodyA.Mass;
                bodyB.Acceleration -= force / bodyB.Mass;
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

        _ = StartSimulationLoop(_cancellationTokenSource.Token);
    }

    private async Task StartSimulationLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Simulation(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task Simulation(CancellationToken cancellationToken)
    {
        const float dt = (float)(ProducerStep / SubSteps);
        var newFramePackets = _latestFramePackets!;

        var step = 0;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            newFramePackets = SimulationFramePackets(newFramePackets, dt);
            step++;
        } while (step < SubSteps);

        cancellationToken.ThrowIfCancellationRequested();

        await _framePacketsChannel.Writer.WriteAsync((_latestFramePackets!, newFramePackets), cancellationToken);

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

                var otherNewPacket = newFramePackets[j];
                var direction = otherNewPacket.Position - newPacket.Position;

                var distanceSquared = direction.LengthSquared() + SofteningFactor;
                var inverseDistance = (float)(1.0 / Math.Sqrt(distanceSquared));
                // var force = direction * (
                //     GravitationalConstant
                //     * oldPacket.Mass
                //     * otherNewPacket.Mass
                //     * inverseDistance
                //     * inverseDistance
                //     * inverseDistance);

                // newAcc += force / oldPacket.Mass;

                var factor = GravitationalConstant
                             * otherNewPacket.Mass
                             * inverseDistance
                             * inverseDistance
                             * inverseDistance;

                newAcc += direction * factor;
            }

            var newVelocity = oldPacket.Velocity
                              + 0.5f * (oldPacket.Acceleration + newAcc) * dt;

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
            bodiesCenter /= _bodies!.Length;

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
