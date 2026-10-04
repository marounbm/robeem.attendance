using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace robeem.Analyzers;

/// <summary>
/// Suppresses StyleCop SA1300 ("element should begin with an uppercase letter") only where the
/// offending identifier is the company brand name <c>robeem</c>, which is all lower-case by
/// branding decision (e.g. the <c>robeem.DataModel</c> namespace). Every other SA1300 finding
/// is still reported at its configured severity.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BrandNameSuppressor : DiagnosticSuppressor
{
    public const string BrandName = "robeem";

    private static readonly SuppressionDescriptor BrandNameRule = new(
        id: "RBM1300",
        suppressedDiagnosticId: "SA1300",
        justification: "'robeem' is the company brand name and is intentionally all lower-case.");

    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions => ImmutableArray.Create(BrandNameRule);

    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        foreach (var diagnostic in context.ReportedDiagnostics)
        {
            var tree = diagnostic.Location.SourceTree;
            if (tree is null)
            {
                continue;
            }

            var token = tree.GetRoot(context.CancellationToken).FindToken(diagnostic.Location.SourceSpan.Start);
            if (token.ValueText == BrandName)
            {
                context.ReportSuppression(Suppression.Create(BrandNameRule, diagnostic));
            }
        }
    }
}
