using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Adn0714And15Record
{
    public long TransactionId { get; set; }

    public string TransactionStatus { get; set; } = null!;

    public decimal SettlementAmount { get; set; }

    public string SettlementCurrency { get; set; } = null!;

    public string SettlementDateTime { get; set; } = null!;

    public double AuthorizationAmount { get; set; }

    public string AuthorizationCurrency { get; set; } = null!;

    public string SubmitDateTime { get; set; } = null!;

    public string? AuthorizationCode { get; set; }

    public long ReferenceTransactionId { get; set; }

    public string TransactionType { get; set; } = null!;

    public string AddressVerificationStatus { get; set; } = null!;

    public string? CardCodeStatus { get; set; }

    public string FraudscreenApplied { get; set; } = null!;

    public string RecurringBillingTransaction { get; set; } = null!;

    public string PartialCaptureStatus { get; set; } = null!;

    public string CardNumber { get; set; } = null!;

    public string ExpirationDate { get; set; } = null!;

    public string? BankAccountNumber { get; set; }

    public string? RoutingNumber { get; set; }

    public double TotalAmount { get; set; }

    public string Currency { get; set; } = null!;

    public string? InvoiceNumber { get; set; }

    public string? InvoiceDescription { get; set; }

    public string CustomerFirstName { get; set; } = null!;

    public string CustomerLastName { get; set; } = null!;

    public string? Company { get; set; }

    public string Address { get; set; } = null!;

    public string? City { get; set; }

    public string? State { get; set; }

    public string Zip { get; set; } = null!;

    public string? Country { get; set; }

    public string? Phone { get; set; }

    public string? Fax { get; set; }

    public string? Email { get; set; }

    public string? CustomerId { get; set; }

    public string? ShipToFirstName { get; set; }

    public string? ShipToLastName { get; set; }

    public string? ShipToCompany { get; set; }

    public string? ShipToAddress { get; set; }

    public string? ShipToCity { get; set; }

    public string? ShipToState { get; set; }

    public string? ShipToZip { get; set; }

    public string? ShipToCountry { get; set; }

    public string? L2Tax { get; set; }

    public string? L2Freight { get; set; }

    public string? L2Duty { get; set; }

    public string L2TaxExempt { get; set; } = null!;

    public string? L2PurchaseOrderNumber { get; set; }

    public string? CavvResultsCode { get; set; }

    public DateOnly BusinessDay { get; set; }

    public string? OrderNumber { get; set; }

    public string? AvailableCardBalance { get; set; }

    public string? ApprovedAmount { get; set; }

    public string MarketType { get; set; } = null!;

    public string Product { get; set; } = null!;

    public string? Reserved7 { get; set; }

    public string? Reserved8 { get; set; }

    public string? Reserved9 { get; set; }

    public string? Reserved10 { get; set; }

    public string? Reserved11 { get; set; }

    public string? Reserved12 { get; set; }

    public string? Reserved13 { get; set; }

    public string? Reserved14 { get; set; }

    public string? Reserved15 { get; set; }

    public string? Reserved16 { get; set; }

    public string? Reserved17 { get; set; }

    public string? Reserved18 { get; set; }

    public string? Reserved19 { get; set; }

    public string? Reserved20 { get; set; }
}
