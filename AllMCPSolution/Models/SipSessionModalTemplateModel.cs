using System;
using System.Collections.Generic;

namespace AllMCPSolution.Models;

public enum SipSessionModalKind
{
    Create,
    Edit
}

public record SipSessionModalTemplateModel(
    SipSessionModalKind Kind,
    string ContainerId,
    string InputIdPrefix,
    string? FormAction = null,
    string? SubmitButtonLabel = null,
    Guid? SisterhoodId = null,
    Guid? SipSessionId = null,
    IReadOnlyList<SipSessionOption>? SisterhoodOptions = null,
    string? Name = null,
    string? ScheduledDateValue = null,
    string? ScheduledTimeValue = null,
    string? Location = null,
    string? Description = null,
    SipSessionModalDeleteModel? Delete = null,
    string? CancelButtonLabel = null);

public record SipSessionOption(Guid Id, string Name);

public record SipSessionModalDeleteModel(
    string Action,
    string ButtonLabel,
    string? ReturnUrl = null);
