import type { PortalLegalPageDto } from '@/types/api'

type LegalPage = 'imprint' | 'privacy'

/**
 * Starting points for the legal pages of the public portal. Placeholders in square brackets must be
 * replaced; an unchanged template is never published (see `withoutUnchangedTemplate`).
 */
export const portalTemplates: Record<LegalPage, string> = {
  imprint: [
    '<h2>Anbieter</h2>',
    '<p><strong>[Name des Dachvereins bzw. der Stadt]</strong><br>[Straße und Hausnummer]<br>[PLZ Ort]</p>',
    '<p>E-Mail: [kontakt@example.org]</p>',
    '<h2>Verantwortlich für den Inhalt</h2>',
    '<p>[Name], Anschrift wie oben.</p>',
    '<h2>Angaben der Vereine</h2>',
    '<p>Vereinsdaten, Ansprechpersonen und Termine tragen die Vereine selbst in Vernetzt ein. Für Korrekturen wende dich bitte an den jeweiligen Verein.</p>'
  ].join(''),
  privacy: [
    '<h2>Verantwortliche Stelle</h2>',
    '<p>[Name des Dachvereins bzw. der Stadt]<br>[Straße und Hausnummer]<br>[PLZ Ort]</p>',
    '<p>Kontakt: [kontakt@example.org]</p>',
    '<h2>Aufruf der Website</h2>',
    '<p>Beim Aufruf verarbeitet der Server technisch notwendige Verbindungsdaten: IP-Adresse, Zeitpunkt, aufgerufene Adresse und Angaben zum Browser. Sie werden für die sichere Auslieferung, zur Begrenzung missbräuchlicher Zugriffe und zur Fehleranalyse verwendet.</p>',
    '<h2>Daten der Vereine</h2>',
    '<p>Angezeigt werden nur Angaben, die in Vernetzt ausdrücklich zur Veröffentlichung freigegeben sind. Ansprechpersonen erscheinen nur, wenn der Verein die Kontaktliste öffentlich geschaltet hat; E-Mail-Adresse und Telefonnummer nur, wenn die Person sie selbst freigegeben hat. Für Korrekturen oder Löschung wende dich an den jeweiligen Verein oder an die verantwortliche Stelle.</p>',
    '<h2>Cookies und externe Dienste</h2>',
    '<p>Diese Website setzt keine Cookies und bindet keine Analyse-, Werbe- oder Schriftdienste Dritter ein. Ob du das helle oder dunkle Farbschema gewählt hast, wird nur in deinem Browser gespeichert.</p>'
  ].join('')
}

/** Fills an empty text page with its template. Returns whether the template was inserted. */
export function applyTemplate(page: LegalPage, value: PortalLegalPageDto): boolean {
  if (value.html.trim()) return false
  value.html = portalTemplates[page]
  return true
}

/** The editor only changes the HTML on user input, so an untouched template still equals the constant. */
export function withoutUnchangedTemplate(page: LegalPage, value: PortalLegalPageDto): PortalLegalPageDto {
  return value.html === portalTemplates[page] ? { ...value, html: '' } : value
}
