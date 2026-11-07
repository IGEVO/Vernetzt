import { api } from '@/services/api'

function decode(value: string): Uint8Array<ArrayBuffer> {
  const normalized = value.replace(/-/g, '+').replace(/_/g, '/')
  const binary = atob(normalized + '='.repeat((4 - normalized.length % 4) % 4))
  return Uint8Array.from(binary, (character) => character.charCodeAt(0))
}

function encode(value: ArrayBuffer | null): string | null {
  if (!value) return null
  const binary = String.fromCharCode(...new Uint8Array(value))
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}

function parseJson(value: string | Record<string, unknown>): Record<string, unknown> {
  if (typeof value !== 'string') return value
  const parsed: unknown = JSON.parse(value)
  return typeof parsed === 'string' ? JSON.parse(parsed) as Record<string, unknown> : parsed as Record<string, unknown>
}

export function passkeysSupported(): boolean {
  return typeof PublicKeyCredential !== 'undefined' && !!navigator.credentials?.get && !!navigator.credentials?.create
}

export async function requestPasskey(optionsJson: string | Record<string, unknown>, conditional = false, signal?: AbortSignal): Promise<string> {
  const json = parseJson(optionsJson)
  const parseNative = PublicKeyCredential.parseRequestOptionsFromJSON
  const publicKey = parseNative ? parseNative(json as unknown as PublicKeyCredentialRequestOptionsJSON) : {
    ...json,
    challenge: decode(json.challenge as string),
    allowCredentials: (json.allowCredentials as PublicKeyCredentialDescriptorJSON[] | undefined)?.map((item) => ({ ...item, id: decode(item.id) }))
  } as PublicKeyCredentialRequestOptions
  const credential = await navigator.credentials.get({ publicKey, mediation: conditional ? 'conditional' : 'optional', signal }) as PublicKeyCredential | null
  if (!credential) throw new Error('Kein Passkey empfangen.')
  const response = credential.response as AuthenticatorAssertionResponse
  return JSON.stringify({
    id: credential.id, rawId: encode(credential.rawId), type: credential.type,
    response: { authenticatorData: encode(response.authenticatorData), clientDataJSON: encode(response.clientDataJSON), signature: encode(response.signature), userHandle: encode(response.userHandle) },
    clientExtensionResults: credential.getClientExtensionResults()
  })
}

export async function createPasskey(optionsJson: string | Record<string, unknown>): Promise<string> {
  const json = parseJson(optionsJson)
  const parseNative = PublicKeyCredential.parseCreationOptionsFromJSON
  const publicKey = parseNative ? parseNative(json as unknown as PublicKeyCredentialCreationOptionsJSON) : {
    ...json,
    challenge: decode(json.challenge as string),
    user: { ...(json.user as PublicKeyCredentialUserEntityJSON), id: decode((json.user as PublicKeyCredentialUserEntityJSON).id) },
    excludeCredentials: (json.excludeCredentials as PublicKeyCredentialDescriptorJSON[] | undefined)?.map((item) => ({ ...item, id: decode(item.id) }))
  } as unknown as PublicKeyCredentialCreationOptions
  const credential = await navigator.credentials.create({ publicKey }) as PublicKeyCredential | null
  if (!credential) throw new Error('Kein Passkey empfangen.')
  const response = credential.response as AuthenticatorAttestationResponse
  return JSON.stringify({
    id: credential.id, rawId: encode(credential.rawId), type: credential.type,
    response: { attestationObject: encode(response.attestationObject), clientDataJSON: encode(response.clientDataJSON), transports: response.getTransports?.() ?? [] },
    clientExtensionResults: credential.getClientExtensionResults()
  })
}

/**
 * Registers a new passkey for the signed-in user (browser dialog + server attestation).
 * Throws if the user cancels or the server rejects the credential.
 */
export async function registerPasskey(name?: string): Promise<void> {
  const options = await api.post<string>('/api/auth/passkeys/creation-options')
  const credentialJson = await createPasskey(options)
  await api.post('/api/auth/passkeys/attestation', { credentialJson, name: name?.trim() || defaultPasskeyName() })
}

function defaultPasskeyName(): string {
  const platform = navigator.platform || 'Gerät'
  return `${platform} – ${new Date().toLocaleDateString('de-DE')}`
}
