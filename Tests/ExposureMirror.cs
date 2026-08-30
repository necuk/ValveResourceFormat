using System;
using ValveResourceFormat.Renderer.SceneEnvironment;

namespace Tests
{
    /// <summary>
    /// Per-frame record of every intermediate value in the CPU auto-exposure chain.
    /// Field order matches the stage order used by the timeline comparison tooling:
    /// average_luminance, raw_scalar, clamp, history, target, speed, log2_dt, compensation, application.
    /// </summary>
    public sealed class ExposureFrame
    {
        /// <summary>Zero-based frame index within the scenario.</summary>
        public int Frame { get; init; }
        /// <summary>Frame delta time in seconds fed to the adaptation integrator.</summary>
        public float DeltaTime { get; init; }
        /// <summary>Scene average luminance owned by an earlier frame's histogram readback.</summary>
        public float AverageLuminance { get; init; }

        /// <summary>Raw exposure scalar, <c>0.18 / AverageLuminance</c>. NaN when the stage did not run.</summary>
        public float RawScalar { get; set; } = float.NaN;
        /// <summary>Whether <see cref="RawScalar"/> was finite, i.e. whether the frame reached the history stage.</summary>
        public bool RawScalarFinite { get; set; }
        /// <summary>Exposure history contents after this frame's push/evict.</summary>
        public float[] History { get; set; } = [];
        /// <summary>Whether the ten-sample weighted window was used instead of the bare clamped raw scalar.</summary>
        public bool HistoryWindowUsed { get; set; }
        /// <summary>Weighted sum of the ten-sample window, or 0 when the window was not used.</summary>
        public float WeightedSum { get; set; }
        /// <summary>Total weight of the ten-sample window, or 0 when the window was not used.</summary>
        public float WeightTotal { get; set; }
        /// <summary>Clamped scalar handed to <see cref="TargetExposure"/>. NaN when the stage did not run.</summary>
        public float ClampedScalar { get; set; } = float.NaN;
        /// <summary>Target exposure after clamping.</summary>
        public float TargetExposure { get; set; }

        /// <summary>Adaptation branch taken: <c>snap</c>, <c>up</c>, <c>down</c>, or <c>bypass</c>.</summary>
        public string SpeedBranch { get; set; } = "bypass";
        /// <summary>Adaptation rate after speed selection and smoothing, before the sign flip and delta-time scale.</summary>
        public float AdaptRateSelected { get; set; }
        /// <summary>Whether the smoothing-range branch reduced the adaptation rate.</summary>
        public bool SmoothingApplied { get; set; }
        /// <summary>Adaptation rate after the sign flip and delta-time scale, as used in the log2 integration.</summary>
        public float AdaptRateScaled { get; set; }
        /// <summary>Whether the overshoot guard clamped the integrated value onto the target.</summary>
        public bool OvershootClamped { get; set; }

        /// <summary>Smoothed exposure after log2 integration.</summary>
        public float CurrentExposure { get; set; }
        /// <summary>Exposure returned by the auto-exposure stage, before compensation.</summary>
        public float ExposureBeforeCompensation { get; set; }
        /// <summary>Linear compensation multiplier, <c>2^ExposureCompensation</c>.</summary>
        public float CompensationScale { get; set; }
        /// <summary>Final linear tonemap scalar uploaded as <c>g_flToneMapScalarLinear</c>.</summary>
        public float TonemapScalar { get; set; }
    }

    /// <summary>
    /// Independent transcription of <c>PostProcessRenderer.CalculateTonemapScalar</c> and
    /// <c>AutoAdjustExposure</c> that also records every intermediate value.
    ///
    /// This mirror exists so a full stage timeline can be produced without changing any
    /// production code. <c>ExposureTimelineTests.MirrorMatchesRenderer</c> asserts that
    /// the mirror reproduces the real renderer bit-exactly at every observable boundary,
    /// which is what makes the mirror's unobservable intermediates admissible evidence.
    /// </summary>
    public sealed class ExposureMirror
    {
        private readonly List<float> history = new(10);

        /// <summary>Gets the mirrored persistent smoothed exposure.</summary>
        public float CurrentExposure { get; private set; } = 1.0f;
        /// <summary>Gets the mirrored persistent target exposure.</summary>
        public float TargetExposure { get; private set; }
        /// <summary>Gets the mirrored exposure history.</summary>
        public IReadOnlyList<float> History => history;

        /// <summary>
        /// Advances the mirror by one frame and returns the full intermediate record.
        /// </summary>
        /// <param name="frame">Frame index recorded into the result.</param>
        /// <param name="settings">Exposure settings for this frame.</param>
        /// <param name="averageLuminance">Scene average luminance from the owning histogram frame.</param>
        /// <param name="deltaTime">Frame delta time in seconds.</param>
        public ExposureFrame Step(int frame, ExposureSettings settings, float averageLuminance, float deltaTime)
        {
            var record = new ExposureFrame
            {
                Frame = frame,
                DeltaTime = deltaTime,
                AverageLuminance = averageLuminance,
            };

            var exposure = AutoAdjust(settings, averageLuminance, deltaTime, record);

            record.ExposureBeforeCompensation = exposure;
            record.CompensationScale = MathF.Pow(2.0f, settings.ExposureCompensation);
            record.TonemapScalar = exposure * record.CompensationScale;
            record.History = [.. history];
            record.CurrentExposure = CurrentExposure;
            return record;
        }

        private float AutoAdjust(ExposureSettings settings, float averageLuminance, float deltaTime, ExposureFrame record)
        {
            const float exposure = 1.0f;

            if (!settings.AutoExposureEnabled)
            {
                record.SpeedBranch = "bypass";
                record.TargetExposure = TargetExposure;
                return exposure;
            }

            var rawScalar = 0.18f / averageLuminance;
            record.RawScalar = rawScalar;
            record.RawScalarFinite = float.IsFinite(rawScalar);

            if (!float.IsFinite(rawScalar))
            {
                // Note: the production path returns the incoming 1.0 here, not CurrentExposure.
                record.SpeedBranch = "bypass";
                record.TargetExposure = TargetExposure;
                return exposure;
            }

            if (history.Count >= 10)
            {
                history.RemoveAt(0);
            }

            history.Add(rawScalar);

            var (min, max) = (settings.ExposureMin, settings.ExposureMax);
            var clampedScalar = Math.Clamp(rawScalar, min, max);

            if (history.Count == 10)
            {
                var weightedSum = 0.0f;
                var weightTotal = 0.0f;

                for (var i = 0; i < 10; i++)
                {
                    var weight = Math.Abs(5 - i) * 0.2f;
                    weightTotal += weight;
                    weightedSum += weight * history[i];
                }

                record.HistoryWindowUsed = true;
                record.WeightedSum = weightedSum;
                record.WeightTotal = weightTotal;
                clampedScalar = Math.Clamp(weightedSum / weightTotal, min, max);
            }

            record.ClampedScalar = clampedScalar;

            if (!float.IsFinite(clampedScalar))
            {
                record.SpeedBranch = "bypass";
                record.TargetExposure = TargetExposure;
                return CurrentExposure;
            }

            TargetExposure = clampedScalar;
            record.TargetExposure = TargetExposure;

            if (settings.ExposureSpeedUp == 0.0)
            {
                record.SpeedBranch = "snap";
                CurrentExposure = TargetExposure;
                return TargetExposure;
            }

            var goingUp = CurrentExposure < TargetExposure;
            var adaptRate = goingUp ? settings.ExposureSpeedUp : settings.ExposureSpeedDown;
            record.SpeedBranch = goingUp ? "up" : "down";

            var logCurrent = MathF.Log2(CurrentExposure);
            var logTarget = MathF.Log2(TargetExposure);
            var logDiff = MathF.Abs(logCurrent - logTarget);

            if (logDiff < settings.ExposureSmoothingRange)
            {
                var smoothed = MathF.Min(logDiff * 0.5f, adaptRate);
                record.SmoothingApplied = smoothed != adaptRate;
                adaptRate = smoothed;
            }

            record.AdaptRateSelected = adaptRate;

            if (CurrentExposure > TargetExposure)
            {
                adaptRate = -adaptRate;
            }

            adaptRate *= deltaTime;
            record.AdaptRateScaled = adaptRate;

            var integrated = MathF.Pow(2, logCurrent + adaptRate);
            var newScalar = adaptRate >= 0.0
                ? MathF.Min(integrated, TargetExposure)
                : MathF.Max(integrated, TargetExposure);
            record.OvershootClamped = newScalar != integrated;

            if (!float.IsFinite(newScalar))
            {
                newScalar = TargetExposure;
            }

            CurrentExposure = newScalar;
            return newScalar;
        }
    }
}
