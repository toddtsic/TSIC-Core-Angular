using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Txs
{
    public int BOldSysTx { get; set; }

    public string TransactionId { get; set; } = null!;

    public string? TransactionStatus { get; set; }

    public string? SettlementAmount { get; set; }

    public string? SettlementCurrency { get; set; }

    public string? SettlementDateTime { get; set; }

    public string? AuthorizationAmount { get; set; }

    public string? AuthorizationCurrency { get; set; }

    public string? SubmitDateTime { get; set; }

    public string? AuthorizationCode { get; set; }

    public string? ReferenceTransactionId { get; set; }

    public string? TransactionType { get; set; }

    public string? AddressVerificationStatus { get; set; }

    public string? CardCodeStatus { get; set; }

    public string? FraudscreenApplied { get; set; }

    public string? RecurringBillingTransaction { get; set; }

    public string? PartialCaptureStatus { get; set; }

    public string? CardNumber { get; set; }

    public string? ExpirationDate { get; set; }

    public string? BankAccountNumber { get; set; }

    public string? RoutingNumber { get; set; }

    public string? TotalAmount { get; set; }

    public string? Currency { get; set; }

    public string? InvoiceNumber { get; set; }

    public string? InvoiceDescription { get; set; }

    public string? CustomerFirstName { get; set; }

    public string? CustomerLastName { get; set; }

    public string? Company { get; set; }

    public string? Address { get; set; }

    public string? City { get; set; }

    public string? State { get; set; }

    public string? Zip { get; set; }

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

    public string? L2TaxExempt { get; set; }

    public string? L2PurchaseOrderNumber { get; set; }

    public string? CavvResultsCode { get; set; }

    public string? BusinessDay { get; set; }

    public string? Reserved2 { get; set; }

    public string? Reserved3 { get; set; }

    public string? Reserved4 { get; set; }

    public string? Reserved5 { get; set; }

    public string? Reserved6 { get; set; }

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
