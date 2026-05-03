using System.Security.Claims;
using System.Text.Json;
using Application.Abstractions.Billing;
using Application.Abstractions.Formulas;
using Application.Billing;
using Application.Formulas.Models;
using Domain.Entities;
using Domain.Repositories;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using SharedLibrary.Common.ResponseModel;
using WebApi.Controllers;

namespace test;

public sealed class PromptFormulasControllerTests
{
    [Fact]
    public async Task Generate_ShouldUseFormulaIdAndPersistAuditLog()
    {
        var userId = Guid.NewGuid();
        var formulaId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var template = new PromptFormulaTemplate
        {
            Id = formulaId,
            Key = "launch-caption",
            Name = "Launch caption",
            Template = "Write about {{product_name}} for {{audience}}.",
            OutputType = "caption",
            DefaultLanguage = "vi",
            DefaultInstruction = "ngắn, chắc, có CTA",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var harness = CreateController(
            userId,
            templateRepository: repo =>
            {
                repo.Setup(x => x.GetByIdAsync(formulaId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(template);
            },
            renderer: renderer =>
            {
                renderer.Setup(x => x.Render(It.IsAny<FormulaTemplateRenderRequest>()))
                    .Returns(Result.Success(new FormulaTemplateRenderResult(
                        "Rendered prompt",
                        new Dictionary<string, string>
                        {
                            ["product_name"] = "MeAI",
                            ["audience"] = "creator"
                        },
                        Array.Empty<string>())));
            },
            generationService: service =>
            {
                service.Setup(x => x.GenerateAsync(
                        It.Is<FormulaGenerationServiceRequest>(request =>
                            request.RenderedPrompt == "Rendered prompt" &&
                            request.OutputType == "caption" &&
                            request.VariantCount == 3),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Result.Success(new FormulaGenerationServiceResult(
                        "gpt-5-4",
                        new[] { "variant 1", "variant 2", "variant 3" })));
            },
            pricingService: pricing =>
            {
                pricing.Setup(x => x.GetCostAsync(
                        CoinActionTypes.FormulaGeneration,
                        "gpt-5-4",
                        null,
                        3,
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Result.Success(new CoinCostQuote(
                        CoinActionTypes.FormulaGeneration,
                        "gpt-5-4",
                        null,
                        "per_variant",
                        2m,
                        3,
                        6m)));
            },
            billingClient: billing =>
            {
                billing.Setup(x => x.DebitAsync(
                        userId,
                        6m,
                        CoinDebitReasons.FormulaGenerationDebit,
                        CoinReferenceTypes.FormulaGeneration,
                        It.IsAny<string>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Result.Success(94m));
            });

        var result = await harness.Controller.Generate(
            new FormulaGenerateRequest
            {
                FormulaId = formulaId,
                OutputType = "caption",
                VariantCount = 3,
                WorkspaceId = workspaceId,
                Variables = new Dictionary<string, JsonElement>
                {
                    ["product_name"] = JsonDocument.Parse("\"MeAI\"").RootElement.Clone(),
                    ["audience"] = JsonDocument.Parse("\"creator\"").RootElement.Clone()
                }
            },
            CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<Result<FormulaGenerateResponse>>().Subject;
        payload.IsSuccess.Should().BeTrue();
        payload.Value.FormulaId.Should().Be(formulaId);
        payload.Value.FormulaKey.Should().Be("launch-caption");
        payload.Value.Outputs.Should().Equal("variant 1", "variant 2", "variant 3");

        harness.TemplateRepositoryMock.VerifyAll();
        harness.GenerationLogRepositoryMock.Verify(x => x.AddAsync(
            It.Is<FormulaGenerationLog>(log =>
                log.UserId == userId &&
                log.WorkspaceId == workspaceId &&
                log.FormulaTemplateId == formulaId &&
                log.FormulaKeySnapshot == "launch-caption" &&
                log.RenderedPrompt == "Rendered prompt" &&
                log.OutputType == "caption" &&
                log.Model == "gpt-5-4" &&
                log.VariablesJson.Contains("MeAI") &&
                log.VariablesJson.Contains("creator")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Generate_ShouldUseFormulaKeyPrecedenceOverInlineTemplate()
    {
        var userId = Guid.NewGuid();
        var template = new PromptFormulaTemplate
        {
            Id = Guid.NewGuid(),
            Key = "hook-template",
            Name = "Hook template",
            Template = "Hook for {{product_name}}",
            OutputType = "hook",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var harness = CreateController(
            userId,
            templateRepository: repo =>
            {
                repo.Setup(x => x.GetByKeyAsync("hook-template", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(template);
            },
            generationService: service =>
            {
                service.Setup(x => x.GenerateAsync(It.IsAny<FormulaGenerationServiceRequest>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Result.Success(new FormulaGenerationServiceResult("gpt-5-4", new[] { "hook" })));
            });

        await harness.Controller.Generate(
            new FormulaGenerateRequest
            {
                FormulaKey = "hook-template",
                Template = "Inline {{product_name}} should be ignored",
                OutputType = "hook",
                Variables = new Dictionary<string, JsonElement>
                {
                    ["product_name"] = JsonDocument.Parse("\"MeAI\"").RootElement.Clone()
                }
            },
            CancellationToken.None);

        harness.RendererMock.Verify(x => x.Render(
            It.Is<FormulaTemplateRenderRequest>(request => request.Template == "Hook for {{product_name}}")), Times.Once);
    }

    [Fact]
    public async Task Generate_ShouldUseInlineTemplateWhenNoFormulaIdOrKey()
    {
        var userId = Guid.NewGuid();
        var harness = CreateController(
            userId,
            generationService: service =>
            {
                service.Setup(x => x.GenerateAsync(It.IsAny<FormulaGenerationServiceRequest>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Result.Success(new FormulaGenerationServiceResult("gpt-5-4", new[] { "outline" })));
            });

        var result = await harness.Controller.Generate(
            new FormulaGenerateRequest
            {
                Template = "Outline for {{topic}}",
                OutputType = "outline",
                Variables = new Dictionary<string, JsonElement>
                {
                    ["topic"] = JsonDocument.Parse("\"AI workflows\"").RootElement.Clone()
                }
            },
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        harness.TemplateRepositoryMock.Verify(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        harness.TemplateRepositoryMock.Verify(x => x.GetByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        harness.RendererMock.Verify(x => x.Render(
            It.Is<FormulaTemplateRenderRequest>(request => request.Template == "Outline for {{topic}}")), Times.Once);
    }

    [Fact]
    public async Task Generate_ShouldReturnMissingVariableErrorName()
    {
        var userId = Guid.NewGuid();
        var harness = CreateController(
            userId,
            renderer: renderer =>
            {
                renderer.Setup(x => x.Render(It.IsAny<FormulaTemplateRenderRequest>()))
                    .Returns(Result.Failure<FormulaTemplateRenderResult>(
                        new Error(
                            "Formula.MissingVariable",
                            "Missing variable: audience.",
                            new Dictionary<string, object?> { ["missingVariable"] = "audience" })));
            });

        var result = await harness.Controller.Generate(
            new FormulaGenerateRequest
            {
                Template = "Caption for {{audience}}",
                OutputType = "caption",
                Variables = new Dictionary<string, JsonElement>()
            },
            CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Type.Should().Be("Formula.MissingVariable");
        var errors = problem.Extensions["errors"].Should().BeAssignableTo<IDictionary<string, object?>>().Subject;
        errors["missingVariable"].Should().Be("audience");
    }

    [Fact]
    public async Task Generate_ShouldBlockInactiveFormula()
    {
        var userId = Guid.NewGuid();
        var formulaId = Guid.NewGuid();
        var harness = CreateController(
            userId,
            templateRepository: repo =>
            {
                repo.Setup(x => x.GetByIdAsync(formulaId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new PromptFormulaTemplate
                    {
                        Id = formulaId,
                        Key = "inactive",
                        Name = "Inactive",
                        Template = "Template",
                        OutputType = "caption",
                        IsActive = false,
                        CreatedAt = DateTime.UtcNow
                    });
            });

        var result = await harness.Controller.Generate(
            new FormulaGenerateRequest
            {
                FormulaId = formulaId,
                OutputType = "caption",
                Variables = new Dictionary<string, JsonElement>()
            },
            CancellationToken.None);

        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Type.Should().Be("Formula.Inactive");
    }

    [Fact]
    public async Task Generate_ShouldRefundOnceWhenGenerationFails()
    {
        var userId = Guid.NewGuid();
        var harness = CreateController(
            userId,
            generationService: service =>
            {
                service.Setup(x => x.GenerateAsync(It.IsAny<FormulaGenerationServiceRequest>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Result.Failure<FormulaGenerationServiceResult>(
                        new Error("Formula.GenerationFailed", "Generation failed.")));
            },
            billingClient: billing =>
            {
                billing.Setup(x => x.RefundAsync(
                        userId,
                        2m,
                        CoinDebitReasons.FormulaGenerationRefund,
                        CoinReferenceTypes.FormulaGeneration,
                        It.IsAny<string>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Result.Success(100m));
            },
            pricingService: pricing =>
            {
                pricing.Setup(x => x.GetCostAsync(
                        CoinActionTypes.FormulaGeneration,
                        "gpt-5-4",
                        null,
                        1,
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Result.Success(new CoinCostQuote(
                        CoinActionTypes.FormulaGeneration,
                        "gpt-5-4",
                        null,
                        "per_variant",
                        2m,
                        1,
                        2m)));
            });

        var result = await harness.Controller.Generate(
            new FormulaGenerateRequest
            {
                Template = "Caption for {{product_name}}",
                OutputType = "caption",
                Variables = new Dictionary<string, JsonElement>
                {
                    ["product_name"] = JsonDocument.Parse("\"MeAI\"").RootElement.Clone()
                }
            },
            CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        harness.BillingClientMock.Verify(x => x.RefundAsync(
            userId,
            2m,
            CoinDebitReasons.FormulaGenerationRefund,
            CoinReferenceTypes.FormulaGeneration,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
        harness.AiSpendRecordRepositoryMock.Verify(x => x.Update(
            It.Is<AiSpendRecord>(record => record.Status == AiSpendStatuses.Refunded)), Times.Once);
        harness.GenerationLogRepositoryMock.Verify(x => x.AddAsync(It.IsAny<FormulaGenerationLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ControllerHarness CreateController(
        Guid userId,
        Action<Mock<IPromptFormulaTemplateRepository>>? templateRepository = null,
        Action<Mock<IFormulaTemplateRenderer>>? renderer = null,
        Action<Mock<IFormulaGenerationService>>? generationService = null,
        Action<Mock<ICoinPricingService>>? pricingService = null,
        Action<Mock<IBillingClient>>? billingClient = null)
    {
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        var templateRepositoryMock = new Mock<IPromptFormulaTemplateRepository>(MockBehavior.Strict);
        var rendererMock = new Mock<IFormulaTemplateRenderer>(MockBehavior.Strict);
        var generationServiceMock = new Mock<IFormulaGenerationService>(MockBehavior.Strict);
        var pricingServiceMock = new Mock<ICoinPricingService>(MockBehavior.Strict);
        var billingClientMock = new Mock<IBillingClient>(MockBehavior.Strict);
        var aiSpendRecordRepositoryMock = new Mock<IAiSpendRecordRepository>(MockBehavior.Strict);
        var generationLogRepositoryMock = new Mock<IFormulaGenerationLogRepository>(MockBehavior.Strict);

        rendererMock
            .Setup(x => x.Render(It.IsAny<FormulaTemplateRenderRequest>()))
            .Returns((FormulaTemplateRenderRequest request) => Result.Success(new FormulaTemplateRenderResult(
                request.Template,
                request.Variables,
                Array.Empty<string>())));

        generationServiceMock
            .Setup(x => x.GenerateAsync(It.IsAny<FormulaGenerationServiceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new FormulaGenerationServiceResult("gpt-5-4", new[] { "generated" })));

        pricingServiceMock
            .Setup(x => x.GetCostAsync(
                CoinActionTypes.FormulaGeneration,
                "gpt-5-4",
                null,
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string actionType, string model, string? variant, int quantity, CancellationToken _) =>
                Result.Success(new CoinCostQuote(actionType, model, variant, "per_variant", 2m, quantity, quantity * 2m)));

        billingClientMock
            .Setup(x => x.DebitAsync(
                userId,
                It.IsAny<decimal>(),
                CoinDebitReasons.FormulaGenerationDebit,
                CoinReferenceTypes.FormulaGeneration,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(100m));

        aiSpendRecordRepositoryMock
            .Setup(x => x.AddAsync(It.IsAny<AiSpendRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        aiSpendRecordRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        aiSpendRecordRepositoryMock
            .Setup(x => x.Update(It.IsAny<AiSpendRecord>()));

        generationLogRepositoryMock
            .Setup(x => x.AddAsync(It.IsAny<FormulaGenerationLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        generationLogRepositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        templateRepository?.Invoke(templateRepositoryMock);
        renderer?.Invoke(rendererMock);
        generationService?.Invoke(generationServiceMock);
        pricingService?.Invoke(pricingServiceMock);
        billingClient?.Invoke(billingClientMock);

        var controller = new PromptFormulasController(
            mediator.Object,
            templateRepositoryMock.Object,
            rendererMock.Object,
            generationServiceMock.Object,
            pricingServiceMock.Object,
            billingClientMock.Object,
            aiSpendRecordRepositoryMock.Object,
            generationLogRepositoryMock.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) },
                        authenticationType: "test"))
                }
            }
        };

        return new ControllerHarness(
            controller,
            templateRepositoryMock,
            rendererMock,
            generationServiceMock,
            pricingServiceMock,
            billingClientMock,
            aiSpendRecordRepositoryMock,
            generationLogRepositoryMock);
    }

    private sealed record ControllerHarness(
        PromptFormulasController Controller,
        Mock<IPromptFormulaTemplateRepository> TemplateRepositoryMock,
        Mock<IFormulaTemplateRenderer> RendererMock,
        Mock<IFormulaGenerationService> GenerationServiceMock,
        Mock<ICoinPricingService> PricingServiceMock,
        Mock<IBillingClient> BillingClientMock,
        Mock<IAiSpendRecordRepository> AiSpendRecordRepositoryMock,
        Mock<IFormulaGenerationLogRepository> GenerationLogRepositoryMock)
    {
        public static implicit operator PromptFormulasController(ControllerHarness harness) => harness.Controller;
    }
}
