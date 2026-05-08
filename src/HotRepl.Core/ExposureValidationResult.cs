using System.Collections.Generic;

namespace HotRepl;

/// <summary>Result of HotRepl network exposure validation.</summary>
public sealed record ExposureValidationResult(bool IsSafeDefault, IReadOnlyList<string> Warnings);
