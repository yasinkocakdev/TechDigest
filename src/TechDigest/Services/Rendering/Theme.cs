namespace TechDigest.Services.Rendering;

/// <summary>
/// Mail ve web sitesinin ortak tasarım dili. Site aynı değerleri site/styles.css içindeki
/// CSS değişkenleriyle kullanır; burada değişen bir renk orada da değişmeli.
/// </summary>
public static class Theme
{
    public const string Font = "-apple-system,BlinkMacSystemFont,'Segoe UI',Inter,Roboto,Helvetica,Arial,sans-serif";

    // Açık tema (mail istemcileri varsayılan olarak bunu görür)
    public const string Page = "#f3f4f7";
    public const string Card = "#ffffff";
    public const string Ink = "#0f172a";
    public const string Body = "#334155";
    public const string Muted = "#64748b";
    public const string Line = "#e5e7eb";
    public const string Soft = "#f8fafc";
    public const string Brand = "#4f46e5";
    public const string Turkish = "#e11d48";
    public const string Highlight = "#fef3c7";

    // Karanlık tema (Apple Mail / iCloud prefers-color-scheme ile)
    public const string DarkPage = "#0b0e14";
    public const string DarkCard = "#141923";
    public const string DarkInk = "#e8ebf1";
    public const string DarkBody = "#c3cad6";
    public const string DarkMuted = "#8b95a7";
    public const string DarkLine = "#252c3a";
    public const string DarkSoft = "#1a2030";
}
