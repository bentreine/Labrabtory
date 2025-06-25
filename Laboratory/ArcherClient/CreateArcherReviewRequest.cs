using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Laboratory.ArcherClient;

public record CreateNewArcherReviewRequest(
        string CaseName,
        string MatterId,
        string MatterName,
        string ClientId,
        string ClientFirstName,
        string ClientLastName,
        string? ClientPhoneNumber,
        string? ClientEmail,
        DateTime? ClientDateOfBirth,
        string InjuredPartyFirstName,
        string InjuredPartyLastName,
        DateTime? InjuredPartyDateOfBirth,
        DateOnly? InjuredPartyDateOfDeath,
        string? InjuredPartyAge
    );
