namespace Application.Formulas;

public static class FormulaOutputTypes
{
    public const string Caption = "caption";
    public const string Hook = "hook";
    public const string Cta = "cta";
    public const string Outline = "outline";
    public const string Custom = "custom";

    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Caption,
        Hook,
        Cta,
        Outline,
        Custom
    };
}
