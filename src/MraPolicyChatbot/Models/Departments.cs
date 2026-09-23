namespace MraPolicyChatbot.Models;

// Admin UI: fixed list of MRA divisions/departments, used to populate the
// Category dropdown on the Upload Policy and Edit Policy forms in place of
// free text. Sourced from MRA's own published structure (two revenue
// divisions plus its supporting divisions) — see careers.mra.mw/content/about.
// This is a static, hardcoded list rather than a database table: it's an
// admin-facing constant that changes rarely, and the project's Policies
// table already stores Category as plain text, so no schema change was
// needed to add this constraint at the UI layer.
public static class Departments
{
    public static readonly string[] All =
    {
        "Customs Division",
        "Domestic Taxes Division",
        "Administration",
        "Corporate Affairs",
        "Debt Management",
        "Enterprise Wide Risk Management",
        "Finance",
        "Human Resource and Organisation Development",
        "Information & Communication Technology (ICT)",
        "Internal Affairs",
        "Internal Audit",
        "Legal Services",
        "Modernisation",
        "Policy Planning & Research",
        "Supply Chain Management",
        "Tax Investigations",
    };
}
