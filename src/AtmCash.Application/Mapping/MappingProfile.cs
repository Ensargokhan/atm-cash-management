using AtmCash.Application.Features.Atms;
using AtmCash.Application.Features.Deposits;
using AtmCash.Application.Features.ExchangeRates;
using AtmCash.Application.Features.Ledger;
using AtmCash.Application.Features.Reconciliations;
using AtmCash.Application.Features.Withdrawals;
using AtmCash.Domain.Atms;
using AtmCash.Domain.ExchangeRates;
using AtmCash.Domain.Ledger;
using AtmCash.Domain.Reconciliations;
using AtmCash.Domain.Withdrawals;
using AutoMapper;

namespace AtmCash.Application.Mapping;

public sealed class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<CurrencyNotes, CurrencyNotesDto>();

        CreateMap<Cassette, CassetteDto>()
            .ForMember(d => d.Contents, o => o.MapFrom(s => s.Contents()));

        CreateMap<Atm, AtmDto>()
            .ForMember(d => d.Balances, o => o.MapFrom(s => AtmBalances.Calculate(s)));
        CreateMap<AtmStatusChange, AtmStatusChangeDto>();

        CreateMap<QuoteOptionNote, NoteDto>();
        CreateMap<QuoteOption, QuoteOptionDto>();
        CreateMap<QuoteReservation, QuoteReservationDto>();
        CreateMap<Quote, QuoteDto>()
            .ForMember(d => d.QuoteId, o => o.MapFrom(s => s.Id))
            .ForMember(d => d.RemainingDailyLimit, o => o.Ignore());

        CreateMap<DepositPlacement, DepositPlacementDto>();

        CreateMap<ReconciliationLine, ReconciliationLineDto>();
        CreateMap<Reconciliation, ReconciliationDto>()
            .ForMember(d => d.ApprovalThresholdTry, o => o.MapFrom(_ => Reconciliation.ApprovalThresholdTry));

        CreateMap<CassetteMovement, MovementDto>();

        CreateMap<ExchangeRate, ExchangeRateDto>();
        CreateMap<ExchangeRateChange, ExchangeRateChangeDto>();
    }
}
