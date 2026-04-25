using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using SharedLibrary.Common.ResponseModel;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace SharedLibrary.Common
{
    public class ApiController : ControllerBase
    {
        protected readonly IMediator _mediator;
        protected ApiController(IMediator mediator)
        {
            _mediator = mediator;
        }

        protected IActionResult HandleFailure(Result result) =>
            result switch
            {
                { IsSuccess: true } => throw new InvalidOperationException(),
                IValidationResult validationResult =>
                    BadRequest(
                        CreateProblemDetails(
                            (int)HttpStatusCode.BadRequest,
                            result.Error,
                            validationResult.Errors
                        )
                    ),
                _ => 
                    BadRequest(
                        CreateProblemDetails(
                            (int)HttpStatusCode.BadRequest,
                            result.Error
                        )
                    ),
            };

        private static ProblemDetails CreateProblemDetails(int status, Error error, Error[]? errors = null)
        {
            var problemDetails = new ProblemDetails
            {
                Status = status,
                Type = error.Code,
                Detail = error.Description
            };

            if (errors != null)
            {
                problemDetails.Extensions[nameof(errors)] = errors;
                return problemDetails;
            }

            if (!string.IsNullOrWhiteSpace(error.Code))
            {
                var errorPayload = new Dictionary<string, object?>
                {
                    ["code"] = error.Code
                };

                if (error.Metadata != null)
                {
                    foreach (var metadata in error.Metadata)
                    {
                        errorPayload[metadata.Key] = metadata.Value;
                    }
                }

                problemDetails.Extensions[nameof(errors)] = errorPayload;
            }

            return problemDetails;
        }
    }
} 
