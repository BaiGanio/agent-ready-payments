namespace Payments.Domain;

public enum PaymentStatus 
{ 
    Created, 
    Authorised,  // British spelling: Authorised, with an s.
    Declined, 
    Voided, 
    PartiallyCaptured, 
    Captured, 
    PartiallyRefunded, 
    Refunded 
}
