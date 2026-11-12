# Provider Adapter Contracts

## Payment adapter
- CreateCheckout(PaymentIntent)
- VerifyWebhook(headers, body, signature)
- QueryTransaction(providerReference)
- Refund(settlementReference, amount, idempotencyKey)
- Reconcile(dateRange)
- GetCapabilities()

## SMS adapter
- Send(recipient, localizedTemplate, variables, idempotencyKey)
- QueryDelivery(providerMessageId)
- ValidateConfiguration()
- ParseDeliveryWebhook()

## Video adapter
- CreateMeeting(event, participants, policy)
- UpdateMeeting(providerMeetingId, event)
- CancelMeeting(providerMeetingId)
- GetJoinDetails(providerMeetingId, participant)
- ValidateWebhook()

Provider adapters must map provider-specific statuses to internal canonical states and retain raw payload only when necessary, encrypted, access-controlled, and retention-limited.
