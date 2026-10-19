create table merchant (
    id               uuid        primary key,
    name             text        not null,
    country          char(2)     not null,
    default_currency char(3)     not null,
    created_at       timestamptz not null default now()
);

create table payment (
    id                uuid          primary key,
    merchant_id       uuid          not null references merchant (id),
    status            text          not null
        check (status in ('Created', 'Authorised', 'Declined', 'Voided',
                          'PartiallyCaptured', 'Captured', 'PartiallyRefunded', 'Refunded')),
    currency          char(3)       not null,
    authorised_amount numeric(19,4) not null check (authorised_amount > 0),
    captured_amount   numeric(19,4) not null default 0,
    refunded_amount   numeric(19,4) not null default 0,
    decline_reason    text          null,
    created_at        timestamptz   not null,
    updated_at        timestamptz   not null
);

create index payment_by_merchant_newest
    on payment (merchant_id, created_at desc, id desc);

create table payment_operation (
    id         uuid          primary key,
    payment_id uuid          not null references payment (id) on delete cascade,
    kind       text          not null
        check (kind in ('Authorise', 'Decline', 'Capture', 'Void', 'Refund')),
    amount     numeric(19,4) not null,
    at         timestamptz   not null
);

create index payment_operation_by_payment
    on payment_operation (payment_id, at);

insert into merchant (id, name, country, default_currency) values
    ('11111111-1111-1111-1111-111111111111', 'M1 Hotel Sofia',    'BG', 'EUR'),
    ('22222222-2222-2222-2222-222222222222', 'M2 Retail Plovdiv', 'BG', 'EUR');