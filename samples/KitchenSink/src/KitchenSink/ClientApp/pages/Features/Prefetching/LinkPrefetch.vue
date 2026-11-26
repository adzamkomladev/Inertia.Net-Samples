<script setup lang="ts">
import { Head, Link } from '@inertiajs/vue3';
import FeatureCard from '@/components/FeatureCard.vue';
import FeatureHeader from '@/components/FeatureHeader.vue';
import AppLayout from '@/layouts/AppLayout.vue';
import { type BreadcrumbItem } from '@/types';

const breadcrumbs: BreadcrumbItem[] = [
    { title: 'Prefetching' },
    { title: 'Link Prefetch' },
];
</script>

<template>
    <Head title="Link Prefetch" />

    <AppLayout :breadcrumbs="breadcrumbs">
        <div class="flex h-full flex-1 flex-col gap-6 p-4">
            <FeatureHeader
                title="Link Prefetch"
                docs="data-props/prefetching#link-prefetching"
                controller="app/Http/Controllers/Feature/PrefetchingController.php#L10"
            >
                Automatic prefetching on mount, hover, and click. Open DevTools
                Network tab to observe prefetch requests.
            </FeatureHeader>

            <div class="grid gap-6 lg:grid-cols-2">
                <!-- Hover (default) -->
                <FeatureCard title="Hover Prefetch (default)">
                    <template #description>
                        <code class="text-xs">prefetch</code>. Prefetches after
                        75ms hover delay.
                    </template>
                    <div class="space-y-3">
                        <Link
                            href="/contacts"
                            prefetch
                            class="inline-flex items-center rounded-md border border-black/10 bg-background px-4 py-2 text-sm font-medium hover:bg-accent dark:border-white/10"
                        >
                            Contacts
                        </Link>
                        <p class="text-xs text-muted-foreground">
                            Hover over the link and watch the Network tab. Data
                            loads before you click.
                        </p>
                    </div>
                </FeatureCard>

                <!-- Click -->
                <FeatureCard title="Click Prefetch">
                    <template #description>
                        <code class="text-xs">prefetch="click"</code>.
                        Prefetches on mousedown, loads by the time click fires.
                    </template>
                    <div class="space-y-3">
                        <Link
                            href="/organizations"
                            prefetch="click"
                            class="inline-flex items-center rounded-md border border-black/10 bg-background px-4 py-2 text-sm font-medium hover:bg-accent dark:border-white/10"
                        >
                            Organizations
                        </Link>
                        <p class="text-xs text-muted-foreground">
                            Data starts loading on mousedown. The page loads
                            almost instantly.
                        </p>
                    </div>
                </FeatureCard>

                <!-- Mount -->
                <FeatureCard title="Mount Prefetch">
                    <template #description>
                        <code class="text-xs">prefetch="mount"</code>.
                        Prefetches immediately when the component mounts.
                    </template>
                    <div class="space-y-3">
                        <Link
                            href="/contacts/create"
                            prefetch="mount"
                            class="inline-flex items-center rounded-md border border-black/10 bg-background px-4 py-2 text-sm font-medium hover:bg-accent dark:border-white/10"
                        >
                            Create Contact
                        </Link>
                        <p class="text-xs text-muted-foreground">
                            This link's page data was prefetched as soon as this
                            page loaded.
                        </p>
                    </div>
                </FeatureCard>

                <!-- cacheFor -->
                <FeatureCard title="Cache Duration">
                    <template #description>
                        <code class="text-xs">cacheFor="10s"</code>. Controls
                        how long prefetched data stays cached.
                    </template>
                    <div class="space-y-3">
                        <Link
                            href="/dashboard"
                            prefetch
                            cacheFor="10s"
                            class="inline-flex items-center rounded-md border border-black/10 bg-background px-4 py-2 text-sm font-medium hover:bg-accent dark:border-white/10"
                        >
                            Dashboard (10s cache)
                        </Link>
                        <p class="text-xs text-muted-foreground">
                            Hover to prefetch, then hover again after 10 seconds
                            to see a new request in the Network tab. Default
                            cache duration is 30 seconds.
                        </p>
                    </div>
                </FeatureCard>
            </div>
        </div>
    </AppLayout>
</template>
