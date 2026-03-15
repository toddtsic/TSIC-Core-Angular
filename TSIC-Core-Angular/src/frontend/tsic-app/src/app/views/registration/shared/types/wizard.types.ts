/**
 * Shared type definitions for wizard codebase.
 * Eliminates ~60+ `any` usages by providing proper interfaces for:
 * - VerticalInsure (VI) widget integration
 * - Syncfusion component events
 * - Raw JSON metadata schemas
 * - Form field/option structures
 */

// ---------------------------------------------------------------------------
// VerticalInsure (VI) Widget Types
// ---------------------------------------------------------------------------

/** Configuration object passed to `new VerticalInsure(offerData)`. */
export interface VIOfferData {
    registrationId?: string;
    sport?: string;
    participantFirstName?: string;
    participantLastName?: string;
    participantDob?: string;
    theme?: {
        colors?: {
            background?: string;
            border?: string;
            cardBackground?: string;
            [key: string]: string | undefined;
        };
        [key: string]: unknown;
    };
    [key: string]: unknown;
}

/** State object emitted by VerticalInsure widget callbacks. */
export interface VIWidgetState {
    quotes?: VIQuoteObject[];
    [key: string]: unknown;
}

/** Individual quote/policy object from VI widget.
 * Unified shape covering both player and team quote responses. */
export interface VIQuoteObject {
    registrationId?: string;
    policyNumber?: string;
    policyCreateDate?: string;
    meta?: VIQuoteMeta;
    participants?: VIQuoteParticipant[];
    quote_id?: string;
    quoteId?: string;
    total?: number;
    id?: string;
    metadata?: Record<string, unknown>;
    policy_attributes?: {
        participant?: { first_name?: string; last_name?: string };
        teams?: Record<string, unknown>[];
    };
    [key: string]: unknown;
}

/** Metadata attached to a VI quote. */
export interface VIQuoteMeta {
    registrationId?: string;
    [key: string]: unknown;
}

/** Participant info within a VI quote. */
export interface VIQuoteParticipant {
    registrationId?: string;
    [key: string]: unknown;
}

/** Global window extension for VerticalInsure constructor. */
export interface VIWindowExtension {
    VerticalInsure?: new (
        hostSelector: string,
        offerData: VIOfferData,
        onReady?: (state: VIWidgetState) => void,
        onChange?: (state: VIWidgetState) => void
    ) => VIWidgetInstance;
}

/** VI widget instance (minimal interface for the methods we call). */
export interface VIWidgetInstance {
    validate(): Promise<boolean>;
    destroy?(): void;
    [key: string]: unknown;
}

// ---------------------------------------------------------------------------
// Raw JSON Metadata Schema Types (from PlayerProfileMetadataJson parsing)
// ---------------------------------------------------------------------------

/** A raw field object parsed from the job's PlayerProfileMetadataJson JSON. */
export interface RawProfileField {
    name?: string;
    dbColumn?: string;
    field?: string;
    label?: string;
    displayName?: string;
    display?: string;
    type?: string;
    inputType?: string;
    required?: boolean;
    options?: RawOptionItem[];
    optionSetName?: string;
    dataSource?: string;
    optionsSource?: string;
    optionSet?: string;
    helpText?: string;
    help?: string;
    placeholder?: string;
    visibility?: string;
    adminOnly?: boolean;
    validation?: { required?: boolean; requiredTrue?: boolean };
    condition?: { field: string; value?: unknown; operator?: string };
    [key: string]: unknown;
}

/** A single option item within a field's option set. */
export interface RawOptionItem {
    value?: string | number;
    Value?: string | number;
    label?: string;
    Label?: string;
    text?: string;
    Text?: string;
    id?: string | number;
    Id?: string | number;
    code?: string;
    Code?: string;
    year?: string | number;
    Year?: string | number;
    [key: string]: unknown;
}

// ---------------------------------------------------------------------------
// USA Lacrosse Validation Types
// ---------------------------------------------------------------------------

/** Debug data displayed in the US Lax modal in player-forms. */
export interface UsLaxDebugData {
    playerId: string;
    membershipNumber: string;
    response?: Record<string, unknown>;
}

// ---------------------------------------------------------------------------
// Credit Card Form Types
// ---------------------------------------------------------------------------

/** Shape of the credit card form value object. */
export interface CreditCardFormValue {
    type: string;
    number: string;
    expiry: string;
    code: string;
    firstName: string;
    lastName: string;
    address: string;
    zip: string;
    email: string;
    phone: string;
}
