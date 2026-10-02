/**
 * Why an attribute no example writes is not a gap — and where it is proved instead.
 *
 * ⚠️ **This is not a list of excuses.** An entry is legitimate only when it names the unit-level
 * proof that stands in for the adoption, and `reasonsWithMissingProofs` refuses one whose tests do
 * not exist in the repository. Without that, the register is the perfect hiding place: a coverage
 * figure with a reason beside it reads as settled, and nobody re-checks a sentence.
 *
 * The rule the entries below come from: *the example declares, the unit suite proves* — except where the reference applications have no honest reason to
 * declare the thing at all, and then the reason has to be readable where the number is. A percentage
 * with an unexplained shortfall is one somebody eventually closes by bending an example, which is the
 * failure the coverage measure exists to stop.
 *
 * An attribute with no entry prints as a gap, because nobody has decided otherwise.
 */

/** @type {Record<string, { why: string, provedBy: string[] }>} */
export const COVERAGE_REASONS = {
  OnBus: {
    why: 'needs a host running two buses at once. Measured: none does — Showcase picks one transport '
      + 'from configuration, and Casework\'s two hosts have one broker each.',
    provedBy: [
      'MultiBusIsolationTests.NamedBus_WithIsolatedTransport_ConsumesOnItsOwnTransport',
      'TheAttributeIsWhatReachesTheGeneratedFileTests.OnBus_RoutesTheSubscriptionAndReplacesTheResolver',
    ],
  },

  MessageMiddleware: {
    why: 'needs a per-message concern the application owns. Every cross-cutting thing Casework\'s '
      + 'handlers need — tenant restoration, idempotency, the audit entry — the framework already '
      + 'does around them.',
    provedBy: [
      'AMiddlewareScopedToOneMessageTypeTests.ForItsOwnMessage_ItRuns',
      'AMiddlewareScopedToOneMessageTypeTests.ForAnotherMessage_ItStepsAsideAndTheHandlerStillRuns',
      'TheAttributeIsWhatReachesTheGeneratedFileTests.MessageMiddleware_RegistersTheMiddlewareInThePipeline',
    ],
  },
};

/**
 * The packages in the same position: no attribute of their own, so the attribute report cannot carry
 * them, and the same decision applies.
 *
 * @type {Record<string, { why: string, provedBy: string[] }>}
 */
export const PACKAGE_REASONS = {
  'Pragmatic.Messaging.ClaimCheck': {
    why: 'needs a large payload on a message. ⚠️ Casework has large data — case documents — and '
      + 'deliberately never puts it on one: the file goes to IFileStorage and the message carries a '
      + 'reference, which is the claim-check pattern done in the domain. An example that also checked '
      + 'one in would teach the same idea twice.',
    provedBy: ['ClaimCheckTests', 'FileStorageClaimCheckStoreTests', 'ClaimCheckEndToEndTests'],
  },

  'Pragmatic.Messaging.Testing': {
    why: 'is a unit layer for messaging. The example suites drive a real broker on purpose, and their '
      + 'value is that they do.',
    provedBy: ['MessageBusTestHarnessTests'],
  },
};

/**
 * Every named proof that does not exist in the repository.
 *
 * ⚠️ The half that keeps this register honest. A reason whose proof was renamed, moved or deleted
 * still reads as settled, and this is the only thing that notices: a proof that is no longer there cannot
 * fail, so nothing else would report it.
 *
 * Matched on the test **name**: a bare class name is satisfied by the class existing, a
 * `Class.Method` by the method's declaration. Both are looked for as written, in test sources only.
 *
 * @param {(pattern: string) => boolean} existsInTests decides whether a name appears in a test source
 * @returns {{ attribute: string, proof: string }[]}
 */
export function reasonsWithMissingProofs(existsInTests) {
  const missing = [];

  for (const [key, entry] of [...Object.entries(COVERAGE_REASONS), ...Object.entries(PACKAGE_REASONS)]) {
    for (const proof of entry.provedBy) {
      const member = proof.includes('.') ? proof.slice(proof.lastIndexOf('.') + 1) : proof;
      if (!existsInTests(member))
        missing.push({ attribute: key, proof });
    }
  }

  return missing;
}
