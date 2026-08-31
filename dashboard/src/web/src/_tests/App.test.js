import { describe, it, expect } from 'vitest'
import { createSSRApp } from 'vue'
import { renderToString } from 'vue/server-renderer'
import { createRouter, createMemoryHistory } from 'vue-router'
import App from '../App.vue'

// Rendered via SSR (mirroring ErrorBanner's test) so the markup contract can
// be asserted without a DOM. App.vue reads its pages and current title from
// the installed router, so each render installs a memory-history router with
// the real routes' metas and stubbed components (router.js itself uses
// createWebHistory, which needs window.location).
const routes = [
  { path: '/', name: 'active', component: { render: () => null }, meta: { title: 'active-jobs', hotkey: '1' } },
  { path: '/issueview', name: 'queue', component: { render: () => null }, meta: { title: 'queue', hotkey: '2' } },
]

async function renderApp(path) {
  const router = createRouter({ history: createMemoryHistory(), routes })
  await router.push(path)
  const app = createSSRApp(App)
  app.use(router)
  return renderToString(app)
}

// Opening anchor tags only: the nav contract lives in the link attributes.
const anchors = html => html.match(/<a\s[^>]*>/g) ?? []
const anchorFor = (html, href) => anchors(html).find(tag => tag.includes(`href="${href}"`))

describe('App primary nav', () => {
  it('is a plain nav of links, not a mismatched ARIA tablist', async () => {
    const html = await renderApp('/')

    expect(html).toContain('<nav')
    expect(html).not.toContain('role="tablist"')
    expect(html).not.toContain('role="tab"')
    expect(html).not.toContain('aria-selected')
  })

  // aria-current="page" and the router's exact-active class (the styling hook
  // for .seg.router-link-exact-active) must land on the same link: the one
  // for the current page, and only that one.
  it.each([
    ['/', '/issueview'],
    ['/issueview', '/'],
  ])('marks %s as the current page link', async (current, other) => {
    const html = await renderApp(current)
    const currentLink = anchorFor(html, current)
    const otherLink = anchorFor(html, other)

    expect(currentLink).toContain('aria-current="page"')
    expect(currentLink).toContain('router-link-exact-active')
    expect(otherLink).not.toContain('aria-current')
    expect(otherLink).not.toContain('router-link-exact-active')
  })
})
