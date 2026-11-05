// SPDX-License-Identifier: Apache-2.0
/** Returns owner-authorised wording, retaining an explicit pending legal review for approval in principle. */
export function approvedCitation(register, category) {
  const entries = register?.entries?.filter(entry => entry.category === category) ?? [];
  const entry = entries[0];
  const authorised = register?.status === 'approved'
    || (register?.status === 'approved-in-principle' && register.legalReview?.status === 'pending');
  if (!authorised || !register.reviewedBy?.trim()
    || !/^\d{4}-\d{2}-\d{2}$/.test(register.reviewedOn ?? '') || entries.length !== 1
    || entry?.status !== register.status || !entry.citation?.title?.trim()
    || !entry.citation.ndpa?.trim() || !entry.citation.gaid?.trim()) {
    throw new Error('The citation requires recorded human approval before use in a production rule.');
  }
  return { ...entry.citation };
}
