/** Human words for the codes the API sends (enum names lowercased). Unknown codes fall back to a spaced, capitalized form. */
const claimKinds: Record<string, string> = { damage: 'Damage', theft: 'Theft', brokenwindow: 'Broken window', other: 'Other' }
const spotTypes: Record<string, string> = { standard: 'Standard', compact: 'Compact', ev: 'EV charging', handicap: 'Accessible', covered: 'Covered' }

const fallback = (code: string) => code.replace(/[_-]+/g, ' ').replace(/^./, c => c.toUpperCase())

export const claimKindLabel = (code: string) => claimKinds[code] ?? fallback(code)
export const spotTypeLabel = (code: string) => spotTypes[code] ?? fallback(code)
