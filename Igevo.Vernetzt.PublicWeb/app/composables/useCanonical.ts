export function useCanonical(path: MaybeRefOrGetter<string>) {
  const config = useRuntimeConfig()
  const canonical = computed(() => new URL(toValue(path), config.public.siteUrl).toString())
  useHead({ link: [{ rel: 'canonical', href: canonical }] })
  return canonical
}
