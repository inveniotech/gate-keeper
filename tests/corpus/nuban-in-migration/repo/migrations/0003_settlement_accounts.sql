-- Operational settlement accounts for the disbursement batch
-- (synthetic ledger-ops environment, nothing here is a live account).
CREATE TABLE IF NOT EXISTS settlement_accounts (
    name TEXT NOT NULL,
    nuban TEXT NOT NULL
);

INSERT INTO settlement_accounts (name, nuban) VALUES ('operations', '5561340446');
INSERT INTO settlement_accounts (name, nuban) VALUES ('treasury', 'ledger-treasury-01');
