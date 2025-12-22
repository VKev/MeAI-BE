using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Application.Ais.Commands;
using FluentValidation;

namespace Application.Ais.Commands
{
    public class CreateAiValidator : AbstractValidator<CreateAiCommand>
    {
        public CreateAiValidator()
        {
            RuleFor(x => x.Email).NotEmpty().MaximumLength(70).EmailAddress();
            RuleFor(x => x.Fullname).NotEmpty().MaximumLength(70);
        }
    }
}