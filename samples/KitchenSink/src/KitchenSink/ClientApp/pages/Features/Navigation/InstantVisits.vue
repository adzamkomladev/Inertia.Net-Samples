<script setup lang="ts">
import { Head, Link, router } from '@inertiajs/vue3';
import CodeBlock from '@/components/CodeBlock.vue';
import FeatureCard from '@/components/FeatureCard.vue';
import FeatureHeader from '@/components/FeatureHeader.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import AppLayout from '@/layouts/AppLayout.vue';
import { type BreadcrumbItem } from '@/types';

defineProps<{
    sourceTimestamp: string;
    message: string;
}>();

const breadcrumbs: BreadcrumbItem[] = [
    { title: 'Navigation' },
    { title: 'Instant Visits' },
];

const targetUrl = '/features/navigation/instant-visit-target?delay=2';

function visitBasic() {
    router.visit(targetUrl, {
        component: 'Features/Navigation/InstantVisitTarget',
    });
}

function visitWithPlaceholderProps() {
    router.visit(targetUrl, {
        component: 'Features/Navigation/InstantVisitTarget',
        pageProps: (_currentProps, sharedProps) => ({
            ...sharedProps,
            greeting: 'Loading from server...',
            serverTimestamp: 'Fetching...',
            items: [],
        }),
    });
}

function visitWithCallbackProps() {
    router.visit(targetUrl, {
        component: 'Features/Navigation/InstantVisitTarget',
        pageProps: (currentProps, sharedProps) => ({
            ...sharedProps,
            greeting: `Navigating from source (was: "${currentProps.message}")`,
            serverTimestamp: 'Waiting for server...',
        }),
    });
}
</script>

<template>
    <Head title="Instant Visits" />

    <AppLayout :breadcrumbs="breadcrumbs">
        <div class="flex h-full flex-1 flex-col gap-6 p-4">
            <FeatureHeader
                title="Instant Visits"
                docs="the-basics/manual-visits#client-side-visits"
                controller="app/Http/Controllers/Feature/NavigationController.php#L79"
            >
                Navigate to a component instantly before the server responds
                using <code class="text-xs">component</code> and
                <code class="text-xs">pageProps</code> options.
            </FeatureHeader>

            <div class="grid gap-6 lg:grid-cols-2">
                <!-- Current state -->
                <FeatureCard
                    info-card
                    title="Current Page State"
                    description="Props on this page (used by the callback demo)."
                >
                    <div class="space-y-3">
                        <div class="flex items-center justify-between">
                            <span class="text-sm font-medium">message</span>
                            <Badge variant="outline">{{ message }}</Badge>
                        </div>
                        <div class="flex items-center justify-between">
                            <span class="text-sm font-medium"
                                >sourceTimestamp</span
                            >
                            <Badge
                                variant="outline"
                                class="font-mono text-xs"
                                >{{ sourceTimestamp }}</Badge
                            >
                        </div>
                    </div>
                </FeatureCard>

                <!-- How it works -->
                <FeatureCard info-card title="How It Works">
                    <div class="space-y-3 text-xs">
                        <div
                            class="rounded-md border border-black/10 p-2 dark:border-white/10"
                        >
                            <p class="font-semibold">1. Instant swap</p>
                            <p class="text-muted-foreground">
                                The target component renders immediately with
                                placeholder or shared props.
                            </p>
                        </div>
                        <div
                            class="rounded-md border border-black/10 p-2 dark:border-white/10"
                        >
                            <p class="font-semibold">2. Server request</p>
                            <p class="text-muted-foreground">
                                The actual HTTP request fires in the background.
                            </p>
                        </div>
                        <div
                            class="rounded-md border border-black/10 p-2 dark:border-white/10"
                        >
                            <p class="font-semibold">3. Props update</p>
                            <p class="text-muted-foreground">
                                When the server responds, real props silently
                                replace the placeholders. Redirects also work
                                correctly.
                            </p>
                        </div>
                    </div>
                </FeatureCard>

                <!-- Basic instant visit -->
                <FeatureCard title="Basic Instant Visit">
                    <template #description>
                        Provide a <code class="text-xs">component</code> name.
                        The page swaps immediately with shared props. The target
                        component should handle missing page-specific props
                        gracefully (e.g. optional chaining). Server response
                        replaces props when ready.
                    </template>
                    <div class="space-y-4">
                        <Button @click="visitBasic">
                            Visit Target (2s delay)
                        </Button>
                        <CodeBlock
                            code="
                            router.visit(targetUrl, {
                              component: 'Features/.../Target',
                            })
                        "
                        />
                    </div>
                </FeatureCard>

                <!-- With placeholder props -->
                <FeatureCard title="With Placeholder Props">
                    <template #description>
                        Use <code class="text-xs">pageProps</code> callback to
                        provide placeholder props. When
                        <code class="text-xs">pageProps</code> is provided,
                        shared props are not automatically carried over, so
                        spread them yourself.
                    </template>
                    <div class="space-y-4">
                        <Button @click="visitWithPlaceholderProps">
                            Visit with Placeholders
                        </Button>
                        <CodeBlock
                            code="
                            router.visit(targetUrl, {
                              component: 'Features/.../Target',
                              pageProps: (_, sharedProps) => ({
                                ...sharedProps,
                                greeting: 'Loading from server...',
                                items: [],
                              }),
                            })
                        "
                        />
                    </div>
                </FeatureCard>

                <!-- With callback props -->
                <FeatureCard title="Callback Props">
                    <template #description>
                        Pass <code class="text-xs">pageProps</code> as a
                        function. Receives current page props and shared props
                        as arguments.
                    </template>
                    <div class="space-y-4">
                        <Button @click="visitWithCallbackProps">
                            Visit with Callback
                        </Button>
                        <CodeBlock>
                            <textarea>
                            router.visit(targetUrl, {
                              component: "Features/.../Target",
                              pageProps: (currentProps, sharedProps) => ({
                                ...sharedProps,
                                greeting: `Navigating from source (was: "${currentProps.message}")`,
                              }),
                            })
                            </textarea>
                        </CodeBlock>
                    </div>
                </FeatureCard>

                <!-- Link component -->
                <FeatureCard title="Link Component">
                    <template #description>
                        Use the <code class="text-xs">component</code> prop on
                        <code class="text-xs">&lt;Link&gt;</code> for
                        declarative instant visits.
                    </template>
                    <div class="space-y-4">
                        <Link
                            :href="targetUrl"
                            component="Features/Navigation/InstantVisitTarget"
                            class="inline-flex h-9 items-center justify-center rounded-md bg-primary px-4 text-sm font-medium text-primary-foreground shadow-xs hover:bg-primary/90"
                        >
                            Link with component
                        </Link>
                        <CodeBlock>
                            <textarea>
                            <Link
                              :href="targetUrl"
                              component="Features/.../Target"
                            >
                              Link with component
                            </Link>
                            </textarea>
                        </CodeBlock>
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
                            title="router.visit()"
                            code="
                            router.visit(url, {
                              component: 'Page/Name',
                              pageProps: { ... },
                            })
                        "
                        />
                        <CodeBlock title="<Link>">
                            <textarea>
                            <Link
                              href="/target"
                              component="Page/Name"
                              :page-props="{ ... }"
                            />
                            </textarea>
                        </CodeBlock>
                        <CodeBlock
                            title="pageProps callback"
                            code="
                            pageProps: (current, shared) => ({
                              ...shared,
                              custom: 'value',
                            })
                        "
                        />
                    </div>
                </FeatureCard>
            </div>
        </div>
    </AppLayout>
</template>
