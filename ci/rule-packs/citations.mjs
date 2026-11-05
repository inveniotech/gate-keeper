// SPDX-License-Identifier: Apache-2.0
/** Returns approved citation wording; drafts and missing review metadata fail closed. */
export function approvedCitation(register, category) {
  const entries = register?.entries?.filter(entry => entry.category === category) ?? [];
  const entry = entries[0];
  if (register?.status !== 'approved' || !register.reviewedBy?.trim()
    || !/^\d{4}-\d{2}-\d{2}$/.test(register.reviewedOn ?? '') || entries.length !== 1
    || entry?.status !== 'approved' || !entry.citation?.title?.trim()
    || !entry.citation.ndpa?.trim() || !entry.citation.gaid?.trim()) {
    throw new Error('The citation requires recorded human approval before use in a production rule.');
  }
  return { ...entry.citation };
}
