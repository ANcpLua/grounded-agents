// Copyright (c) ANcpLua. Licensed under the MIT License.

namespace ANcpLua.Agents.Evaluation;

/// <summary>
/// The verdict of a custom check, and — when the check bothers to say — why.
/// </summary>
/// <remarks>
/// This exists so <see cref="EvaluationSuite.Custom"/> can accept both a bare <see cref="bool"/> and a
/// <c>(bool, string)</c> tuple through a <em>single</em> method. Two overloads differing only in the
/// delegate's return type would be ambiguous for any lambda whose body is a <c>throw</c> expression or
/// whose return type needs inference, which is exactly the kind of paper cut a check-writing API cannot
/// afford.
/// </remarks>
/// <param name="Passed">Whether the item passed the check.</param>
/// <param name="Reason">
/// The explanation shown in the report, or <see langword="null"/> to fall back to a bare
/// <c>Passed</c>/<c>Failed</c>.
/// </param>
public readonly record struct CheckOutcome(bool Passed, string? Reason = null)
{
    /// <summary>Converts a bare verdict into an outcome with no explanation.</summary>
    /// <param name="passed">Whether the item passed.</param>
    public static implicit operator CheckOutcome(bool passed) => new(passed);

    /// <summary>Converts a verdict-and-reason tuple into an outcome.</summary>
    /// <param name="outcome">The verdict and its explanation.</param>
    public static implicit operator CheckOutcome((bool Passed, string Reason) outcome) =>
        new(outcome.Passed, outcome.Reason);

    /// <summary>Creates a passing outcome.</summary>
    /// <param name="reason">The explanation shown in the report.</param>
    public static CheckOutcome Pass(string? reason = null) => new(true, reason);

    /// <summary>Creates a failing outcome.</summary>
    /// <param name="reason">The explanation shown in the report.</param>
    public static CheckOutcome Fail(string? reason = null) => new(false, reason);
}
