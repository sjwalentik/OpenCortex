namespace OpenCortex.Core.Query;

public sealed record OqlFilter(string Field, string Operator, string Value);
