import { setResponseHeader } from 'h3'

// Internal route for the cached portal settings; server rendering calls it in-process.
export default defineEventHandler(async (event) => {
  setResponseHeader(event, 'cache-control', 'public, max-age=60')
  return await fetchPortal(event)
})
