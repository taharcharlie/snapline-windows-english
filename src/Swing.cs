// Snapline: a local screenshot shelf for Windows. MIT License.
// Fun animation: each card is a small damped pendulum on its peg, like clothes on a line. It swings when the line glides,
// when it is dragged, when the pointer brushes past, when a gust runs along the line, and when it is newly hung.
// The loop runs only while something moves; between events the cards rest and nothing is redrawn.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Snapline
{
    sealed class Pendulum
    {
        internal double Angle, Speed;      // degrees and degrees per second, around the peg
        internal double Target;            // where it settles: the card's lean, or upright while the pointer is on it
        internal double Y, YSpeed;         // the drop of a newly hung card
        internal double LastX, LastVelocity;
        internal bool Sampled;
        internal DateTime BrushedAt;
        // Spring and damping give a swing of a little under one per second that dies away within about a second and a half.
        internal const double Spring = 32, Damping = 4.6, DriveGain = 0.024, MaxSwing = 8;
        // One integration step: the spring pulls toward Target, damping slows it, and the peg's sideways
        // acceleration (pixels per second squared) pushes the card the other way, as a hanging thing does.
        internal static void Step(Pendulum p, double accel, double seconds)
        {
            double a = -Spring * (p.Angle - p.Target) - Damping * p.Speed - accel * DriveGain;
            p.Speed += a * seconds; p.Angle += p.Speed * seconds;
            double low = p.Target - MaxSwing, high = p.Target + MaxSwing;
            if (p.Angle < low) { p.Angle = low; if (p.Speed < 0) p.Speed = 0; }
            if (p.Angle > high) { p.Angle = high; if (p.Speed > 0) p.Speed = 0; }
            double ay = -70 * p.Y - 10 * p.YSpeed; // A stiffer, quicker spring for the drop.
            p.YSpeed += ay * seconds; p.Y += p.YSpeed * seconds;
        }
        internal bool Resting { get { return Math.Abs(Angle - Target) < 0.03 && Math.Abs(Speed) < 0.05 && Math.Abs(Y) < 0.1 && Math.Abs(YSpeed) < 0.5; } }
    }

    sealed partial class Shelf
    {
        static readonly object TipsKey = new object();
        static readonly Random swingRandom = new Random();
        readonly Dictionary<object, Pendulum> pendulums = new Dictionary<object, Pendulum>();
        HashSet<object> knownOnLine;
        bool swingAwake;
        DateTime swingLast, swingQuietSince;
        DispatcherTimer gustTimer;
        double gustX = Double.NaN, gustDirection;

        Pendulum PendulumFor(HangingMotion motion)
        {
            Pendulum p;
            if (!pendulums.TryGetValue(motion.Key, out p)) { p = new Pendulum { Angle = motion.Rest, Target = motion.Rest }; pendulums[motion.Key] = p; }
            return p;
        }
        // Called as a card is hung, in Fun: the pendulum takes over the angle from any running animation.
        void AttachSwing(HangingMotion motion)
        {
            var p = PendulumFor(motion);
            if (!motion.Card.IsMouseOver) p.Target = motion.Rest;
            motion.Swing.Angle = p.Angle;
            Point last = new Point(Double.NaN, 0); DateTime lastAt = DateTime.Now;
            motion.Card.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!Theme.Fun || hoverEffectsPaused || dragging || reorderSlots != null) return;
                var at = e.GetPosition(canvas); DateTime now = DateTime.Now;
                double seconds = (now - lastAt).TotalSeconds;
                if (!Double.IsNaN(last.X) && seconds > 0.004 && seconds < 0.2)
                {
                    double vx = (at.X - last.X) / seconds;
                    // A quick pass of the pointer brushes the card aside a little, at most once in a while.
                    if (Math.Abs(vx) > 300 && (now - p.BrushedAt).TotalMilliseconds > 180)
                    { p.Speed += Math.Max(-14, Math.Min(14, vx * 0.012)); p.BrushedAt = now; WakeSwing(); }
                }
                last = at; lastAt = now;
            };
        }
        // The hover straighten and its release become where the pendulum settles.
        void SwingToward(HangingMotion motion, double target) { PendulumFor(motion).Target = target; WakeSwing(); }

        void WakeSwing()
        {
            if (swingAwake || !Theme.Fun || !expanded || shuttingDown) return;
            swingAwake = true; swingLast = swingQuietSince = DateTime.Now;
            foreach (var p in pendulums.Values) p.Sampled = false;
            CompositionTarget.Rendering += SwingFrame;
        }
        void SleepSwing()
        {
            if (swingAwake) CompositionTarget.Rendering -= SwingFrame;
            swingAwake = false;
        }
        void SwingFrame(object sender, EventArgs e)
        {
            if (!swingAwake) return;
            if (!expanded || !Theme.Fun || shuttingDown) { SleepSwing(); return; }
            DateTime now = DateTime.Now; double seconds = (now - swingLast).TotalSeconds;
            if (seconds < 1.0 / 62) return; // At most about sixty updates a second, whatever the display's rate.
            swingLast = now; seconds = Math.Min(seconds, 0.1);
            if (hoverEffectsPaused) return;
            bool moving = false;
            double gustFrom = gustX;
            if (!Double.IsNaN(gustX))
            {
                gustX += gustDirection * 900 * seconds;
                if (gustX < -300 || gustX > shelfWidth + 300) gustX = Double.NaN; else moving = true;
            }
            foreach (var motion in hangingMotions)
            {
                var p = PendulumFor(motion);
                double x = Canvas.GetLeft(motion.Card) + motion.Card.Width / 2, accel = 0;
                if (Double.IsNaN(x)) continue;
                if (p.Sampled)
                {
                    double velocity = (x - p.LastX) / seconds;
                    accel = Math.Max(-20000, Math.Min(20000, (velocity - p.LastVelocity) / seconds));
                    p.LastVelocity = velocity;
                }
                else { p.LastVelocity = 0; p.Sampled = true; }
                p.LastX = x;
                // A gust gives each card a push as it passes, a little different for each.
                if (!Double.IsNaN(gustFrom) && !Double.IsNaN(gustX) && Math.Min(gustFrom, gustX) <= x && x < Math.Max(gustFrom, gustX))
                    p.Speed += gustDirection * (6 + swingRandom.NextDouble() * 6);
                int steps = (int)Math.Ceiling(seconds / 0.008);
                for (int i = 0; i < steps; i++) Pendulum.Step(p, accel, seconds / steps);
                motion.Swing.Angle = p.Angle;
                if (motion.Fall != null) motion.Fall.Y = p.Y;
                if (!p.Resting || Math.Abs(accel) > 1) moving = true;
            }
            if (moving) swingQuietSince = now;
            else if ((now - swingQuietSince).TotalSeconds > 0.4)
            {
                foreach (var motion in hangingMotions) { var p = PendulumFor(motion); p.Angle = p.Target; p.Speed = 0; p.Y = p.YSpeed = 0; motion.Swing.Angle = p.Angle; if (motion.Fall != null) motion.Fall.Y = 0; }
                SleepSwing();
            }
        }
        // Every so often while the line is open in Fun, a gust runs along it from one end.
        void ScheduleGust(bool on)
        {
            if (!on) { if (gustTimer != null) gustTimer.Stop(); gustX = Double.NaN; return; }
            if (gustTimer == null)
            {
                gustTimer = new DispatcherTimer();
                gustTimer.Tick += delegate
                {
                    gustTimer.Interval = TimeSpan.FromSeconds(9 + swingRandom.NextDouble() * 11);
                    if (!expanded || !Theme.Fun || hoverEffectsPaused || dragging || reorderSlots != null || gliding) return;
                    gustDirection = swingRandom.Next(2) == 0 ? -1 : 1;
                    gustX = gustDirection > 0 ? -250 : shelfWidth + 250;
                    WakeSwing();
                };
            }
            if (!gustTimer.IsEnabled) { gustTimer.Interval = TimeSpan.FromSeconds(9 + swingRandom.NextDouble() * 11); gustTimer.Start(); }
        }
        // After a redraw: a card that was not on the line before drops onto it; pendulums of cards that left are forgotten.
        void SettleSwings(List<object> deck)
        {
            if (!Theme.Fun) { pendulums.Clear(); knownOnLine = null; ScheduleGust(false); SleepSwing(); return; }
            var now = new HashSet<object>(deck);
            if (knownOnLine != null)
                foreach (var motion in hangingMotions)
                    if (motion.Key != TipsKey && !knownOnLine.Contains(motion.Key))
                    {
                        var p = PendulumFor(motion); p.Y = -26; p.YSpeed = 0; p.Speed += 12;
                        if (motion.Fall != null) motion.Fall.Y = p.Y;
                    }
            knownOnLine = now;
            foreach (var key in pendulums.Keys.ToList()) if (key != TipsKey && !now.Contains(key)) pendulums.Remove(key);
            ScheduleGust(true);
            if (pendulums.Values.Any(x => !x.Resting)) WakeSwing();
        }
        // The line closed: cards come back at rest next time.
        void RestSwings() { SleepSwing(); ScheduleGust(false); pendulums.Clear(); }
    }
}
