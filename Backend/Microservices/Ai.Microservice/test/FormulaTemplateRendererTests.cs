using Application.Abstractions.Formulas;
using FluentAssertions;
using Infrastructure.Logic.Formulas;

namespace test;

public sealed class FormulaTemplateRendererTests
{
    private readonly FormulaTemplateRenderer _renderer = new();

    [Fact]
    public void Render_ShouldReplacePlaceholdersCaseInsensitively()
    {
        var result = _renderer.Render(new FormulaTemplateRenderRequest(
            "Write for {{Product_Name}} and {{AUDIENCE}}",
            new Dictionary<string, string>
            {
                ["product_name"] = "MeAI",
                ["audience"] = "creator"
            },
            "caption",
            "vi",
            "ngắn"));

        result.IsSuccess.Should().BeTrue();
        result.Value.RenderedPrompt.Should().Contain("Output type: caption.");
        result.Value.RenderedPrompt.Should().Contain("Language: vi.");
        result.Value.RenderedPrompt.Should().Contain("Instruction: ngắn.");
        result.Value.RenderedPrompt.Should().Contain("Write for MeAI and creator");
    }

    [Fact]
    public void Render_ShouldReturnMissingVariableName()
    {
        var result = _renderer.Render(new FormulaTemplateRenderRequest(
            "Write for {{product_name}} and {{audience}}",
            new Dictionary<string, string>
            {
                ["product_name"] = "MeAI"
            },
            "caption",
            null,
            null));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Formula.MissingVariable");
        result.Error.Metadata.Should().ContainKey("missingVariable");
        result.Error.Metadata!["missingVariable"].Should().Be("audience");
    }
}
