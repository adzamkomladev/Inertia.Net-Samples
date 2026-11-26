<script setup lang="ts">
import { Head, Link, router, usePage } from '@inertiajs/vue3';
import { computed } from 'vue';
import CodeBlock from '@/components/CodeBlock.vue';
import FeatureCard from '@/components/FeatureCard.vue';
import FeatureHeader from '@/components/FeatureHeader.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import AppLayout from '@/layouts/AppLayout.vue';
import { type BreadcrumbItem } from '@/types';

defineProps<{
    timestamp: string;
    redirectedFrom?: string;
}>();

const page = usePage();
const currentUrl = computed(() => page.url);

const breadcrumbs: BreadcrumbItem[] = [
    { title: 'Navigation' },
    { title: 'URL Fragments' },
];

function hashRedirectGet() {
    router.get('/features/navigation/url-fragments/redirect-hash');
}

function hashRedirectPost() {
    router.post('/features/navigation/url-fragments/redirect-hash');
}

function preserveFragmentVisit() {
    router.visit(
        '/features/navigation/url-fragments/preserve-redirect#my-fragment',
    );
}
</script>

<template>
    <Head title="URL Fragments" />

    <AppLayout :breadcrumbs="breadcrumbs">
        <div class="flex h-full flex-1 flex-col gap-6 p-4">
            <FeatureHeader
                title="URL Fragments"
                docs="the-basics/redirects#preserving-fragments"
                controller="app/Http/Controllers/Feature/NavigationController.php#L147"
            >
                Hash fragment support in redirects. Server-directed fragments
                and client-side fragment preservation.
            </FeatureHeader>

            <!-- Current URL display -->
            <FeatureCard
                title="Current URL"
                description="The current page URL including any hash fragment."
            >
                <div class="space-y-3">
                    <div class="flex items-center gap-2">
                        <Badge variant="outline" class="font-mono text-xs">{{
                            currentUrl
                        }}</Badge>
                        <Badge
                            v-if="redirectedFrom"
                            variant="default"
                            class="text-xs"
                            >Redirected via {{ redirectedFrom }}</Badge
                        >
                    </div>
                    <div class="flex items-center gap-2">
                        <span class="text-sm text-muted-foreground"
                            >Timestamp:</span
                        >
                        <Badge variant="secondary" class="font-mono text-xs">{{
                            timestamp
                        }}</Badge>
                    </div>
                </div>
            </FeatureCard>

            <div class="grid gap-6 lg:grid-cols-2">
                <!-- Hash Fragment Redirect (PR #2899) -->
                <FeatureCard
                    title="Hash Fragment Redirect"
                    description="Server redirects to a URL with a hash fragment. The middleware detects the fragment and ensures Inertia navigates to the correct URL including the hash."
                >
                    <div class="space-y-4">
                        <div class="flex flex-wrap gap-2">
                            <Button @click="hashRedirectGet">
                                GET Redirect with #hash
                            </Button>
                            <Button variant="outline" @click="hashRedirectPost">
                                POST Redirect with #hash
                            </Button>
                        </div>
                        <p class="text-xs text-muted-foreground">
                            The server redirects to
                            <code class="font-mono"
                                >/url-fragments#server-section</code
                            >. Check the URL bar after clicking.
                        </p>
                        <CodeBlock
                            title="Server"
                            code="return redirect('/page#section');
// Middleware detects the hash fragment
// Ensures the client navigates to the
// URL with the fragment intact"
                        />
                    </div>
                </FeatureCard>

                <!-- Preserve Fragment (PR #2897) -->
                <FeatureCard title="Preserve Fragment">
                    <template #description>
                        Navigate with a hash fragment through a server redirect.
                        <code class="text-xs">preserveFragment()</code> keeps
                        the client-side fragment intact on the final URL.
                    </template>
                    <div class="space-y-4">
                        <div class="flex flex-wrap gap-2">
                            <Button @click="preserveFragmentVisit">
                                Visit with #my-fragment
                            </Button>
                            <Link
                                href="/features/navigation/url-fragments/preserve-redirect#link-fragment"
                                class="inline-flex h-9 items-center justify-center rounded-md border border-black/10 bg-background px-4 text-sm font-medium hover:bg-accent hover:text-accent-foreground dark:border-white/10"
                            >
                                Link with #link-fragment
                            </Link>
                        </div>
                        <p class="text-xs text-muted-foreground">
                            The server redirects to a different URL, but the
                            original hash fragment survives.
                        </p>
                        <CodeBlock
                            title="Server"
                            code="// Redirect source
return redirect('/target')
    ->preserveFragment();

// Target page responds normally
// Client applies #fragment from original URL"
                        />
                    </div>
                </FeatureCard>

                <!-- Target section for hash redirect -->
                <FeatureCard
                    info-card
                    id="server-section"
                    title="Server Section"
                >
                    <template #description>
                        This card has
                        <code class="text-xs">id="server-section"</code>. The
                        hash redirect scrolls here.
                    </template>
                    <p class="text-sm text-muted-foreground">
                        If you arrived via the hash redirect buttons above, the
                        URL should contain
                        <code class="font-mono text-xs">#server-section</code>
                        and the browser scrolled to this card.
                    </p>
                </FeatureCard>

                <!-- How it works -->
                <FeatureCard info-card title="Key Differences">
                    <div class="space-y-3 text-xs">
                        <div
                            class="rounded-md border border-black/10 p-2 dark:border-white/10"
                        >
                            <p class="font-semibold">Hash Fragment Redirect</p>
                            <p class="text-muted-foreground">
                                The <em>server</em> decides the hash fragment.
                                Redirect response includes
                                <code>#section</code> in the URL. Middleware
                                converts to SPA visit.
                            </p>
                        </div>
                        <div class="rounded-md border-2 border-primary p-2">
                            <p class="font-semibold">preserveFragment</p>
                            <p class="text-muted-foreground">
                                The <em>client</em> supplies the hash fragment.
                                The server calls
                                <code>->preserveFragment()</code> on the
                                redirect to keep the original fragment on the
                                final URL.
                            </p>
                        </div>
                    </div>
                </FeatureCard>

                <!-- API Reference -->
                <FeatureCard
                    info-card
                    class="lg:col-span-2"
                    title="API Reference"
                >
                    <div class="grid gap-3 sm:grid-cols-3">
                        <CodeBlock
                            title="Hash Redirect (server)"
                            code="// Standard Laravel redirect
return redirect('/page#hash');

// Middleware auto-detects hash
// and navigates to the URL
// with the fragment intact"
                        />
                        <CodeBlock
                            title="preserveFragment (server)"
                            code="return redirect('/target')
    ->preserveFragment();

// Stores flag in session
// Target page includes
// preserveFragment: true"
                        />
                        <CodeBlock
                            title="Client usage"
                            code="// Hash redirect. Automatic
router.get('/redirect-with-hash')

// preserveFragment
router.visit('/redirect#frag')
// #frag survives the redirect"
                        />
                    </div>
                </FeatureCard>
            </div>
        </div>
    </AppLayout>
</template>
