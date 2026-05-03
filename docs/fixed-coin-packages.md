# Fixed Coin Packages

## Public catalog for frontend

User coin packages are stored in `coin_packages` and exposed through `GET /api/User/billing/coin-packages`.

Public response fields:
- `id`
- `name`
- `coinAmount`
- `bonusCoins`
- `totalCoins`
- `price`
- `currency`
- `displayOrder`

Only active packages are returned to the public catalog. Results are sorted by `displayOrder`, then by `createdAt`.

## Checkout flow

Endpoint: `POST /api/User/billing/coin-packages/{packageId}/checkout`

Flow:
1. Validate the package exists, is active, has a positive price, and uses `usd`.
2. Resolve or create the Stripe customer for the authenticated user.
3. Create a pending `Transaction` with:
   - `RelationType = "CoinPackage"`
   - `RelationId = packageId`
   - `PaymentMethod = "Stripe"`
   - `TransactionType = "coin_package_purchase"`
4. Create a Stripe one-time `PaymentIntent`.
5. Persist Stripe identifiers back to the same `Transaction`:
   - `ProviderReferenceId = paymentIntentId`
   - `Status = Stripe payment intent status`
6. Return checkout payload to frontend:
   - `packageId`
   - `transactionId`
   - `paymentIntentId`
   - `clientSecret`
   - `status`
   - `amountDue`
   - `currency`

Stripe metadata for this flow:
- `flow_type = coin_package`
- `user_id`
- `coin_package_id`
- `transaction_id`

## Resolve flow

Endpoint: `POST /api/User/billing/coin-packages/resolve-checkout`

Flow:
1. Frontend submits `paymentIntentId` and may also send `packageId` and `transactionId`.
2. Backend queries Stripe using the coin-package checkout status API.
3. Backend forwards the normalized result into the shared confirm command.
4. The confirm command updates the payment `Transaction` status.
5. Only successful Stripe states can credit coins.
6. Response includes:
   - `packageId`
   - `transactionId`
   - `paymentIntentId`
   - `status`
   - `isFinal`
   - `coinsCredited`
   - `alreadyCredited`
   - `creditedCoins`
   - `currentBalance`

## Webhook flow

Endpoint: `POST /api/User/webhooks/stripe`

Coin-package webhooks reuse the same confirm command as the resolve endpoint.

Rules:
- Webhook handling detects coin-package payments from Stripe metadata `flow_type = coin_package`.
- `payment_intent.succeeded` is routed into `ConfirmCoinPackagePaymentCommand`.
- Webhook processing is not allowed to credit coins through a separate code path.
- Confirm logic remains the single write path for top-up crediting.

## Transaction and ledger mapping

`Transaction` is the payment audit record and remains the payment source of truth.

For a coin-package purchase it stores:
- `RelationType = "CoinPackage"`
- `RelationId = CoinPackage.Id`
- `PaymentMethod = "Stripe"`
- `TransactionType = "coin_package_purchase"`
- `ProviderReferenceId = Stripe PaymentIntentId`
- `Status = normalized Stripe checkout status`

`CoinTransaction` is the balance ledger source of truth.

For a successful coin-package credit it stores:
- `Delta = CoinAmount + BonusCoins`
- `Reason = "coin_package.purchase"`
- `ReferenceType = "coin_package"`
- `ReferenceId = Transaction.Id`
- `BalanceAfter = resulting user balance`

## Idempotency rules

Coin crediting is business-idempotent per transaction.

Rules:
- The confirm command is the only place allowed to apply the coin credit.
- Deduplication is enforced through the billing ledger check before inserting a new `CoinTransaction`.
- The dedupe key is effectively:
  - `UserId`
  - `Reason = "coin_package.purchase"`
  - `ReferenceType = "coin_package"`
  - `ReferenceId = Transaction.Id`
- If the same transaction is resolved twice, the second call is treated as a successful no-op.
- If both webhook and resolve execute for the same purchase, only the first successful credit mutates balance; the replay reports `alreadyCredited = true`.

## Admin APIs

Admin management endpoints:
- `GET /api/User/admin/billing/coin-packages`
- `POST /api/User/admin/billing/coin-packages`
- `PUT /api/User/admin/billing/coin-packages/{packageId}`
- `DELETE /api/User/admin/billing/coin-packages/{packageId}`

Behavior:
- Admin list returns both active and inactive packages.
- Create supports fixed package fields: `name`, `coinAmount`, `bonusCoins`, `price`, `currency`, `isActive`, `displayOrder`.
- Update edits the same fixed-package fields.
- Delete is a soft deactivation for the catalog by setting `IsActive = false`.
- Package management is isolated from subscription plan management.

## Difference from subscription purchase flow

Coin-package purchase differs from subscription purchase in these ways:
- Coin packages use Stripe one-time `PaymentIntent`; subscriptions use recurring Stripe subscription flows.
- Coin packages credit `User.MeAiCoin` immediately after a successful payment confirmation.
- Coin packages append a `CoinTransaction` ledger entry for balance movement.
- Coin-package payment state is audited in `Transaction`, but there is no recurring entitlement lifecycle.
- Subscription flows update subscription state and Stripe subscription references instead of minting spendable coin balance.

## Validation and error behavior

Main error cases handled by the backend:
- package not found or inactive
- invalid package price
- unsupported currency
- payment intent mismatch with transaction
- missing package reference on transaction
- package mismatch between request and transaction
- Stripe status lookup failures

Successful non-final or non-succeeded resolve attempts still update the payment `Transaction` status, but they do not credit user coins until Stripe reports a successful final payment state.
