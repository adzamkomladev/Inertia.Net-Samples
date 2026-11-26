<script setup lang="ts">
import { Head, Link } from '@inertiajs/vue3';
import FeatureCard from '@/components/FeatureCard.vue';
import FeatureHeader from '@/components/FeatureHeader.vue';
import AppLayout from '@/layouts/AppLayout.vue';
import { type BreadcrumbItem } from '@/types';

const breadcrumbs: BreadcrumbItem[] = [
    { title: 'Prefetching' },
    { title: 'Stale While Revalidate' },
];
</script>

<template>
    <Head title="Stale While Revalidate" />

    <AppLayout :breadcrumbs="breadcrumbs">
        <div class="flex h-full flex-1 flex-col gap-6 p-4">
            <FeatureHeader
                title="Stale While Revalidate"
                docs="data-props/prefetching#stale-while-revalidate"
                controller="app/Http/Controllers/Feature/PrefetchingController.php#L15"
            >
                SWR caching strategy. Serve stale data instantly while
                revalidating in the background.
            </FeatureHeader>

            <div class="grid gap-6 lg:grid-cols-2">
                <FeatureCard title="SWR Link">
                    <template #description>
                        <code class="text-xs">:cacheFor="['10s', '20s']"</code>.
                        Prefetched on mount. 10s fresh, then stale for up to
                        20s.
                    </template>
                    <div class="space-y-3">
                        <Link
                            href="/contacts"
                            prefetch="mount"
                            :cacheFor="['10s', '20s']"
                            class="inline-flex items-center rounded-md border border-black/10 bg-background px-4 py-2 text-sm font-medium hover:bg-accent dark:border-white/10"
                        >
                            Contacts (10s/20s SWR)
                        </Link>
                        <p class="text-xs text-muted-foreground">
                            Prefetched on mount. Within 10s, clicking navigates
                            instantly from cache. Between 10-20s, stale data is
                            served immediately and the page updates with fresh
                            data. After 20s, the cache is gone.
                        </p>
                    </div>
                </FeatureCard>

                <FeatureCard title="How SWR Works">
                    <template #description>
                        Pass a tuple to <code class="text-xs">cacheFor</code>:
                        <code class="text-xs">[freshPeriod, stalePeriod]</code>.
                    </template>
                    <div class="space-y-3">
                        <div class="rounded-md bg-muted p-4 text-sm">
                            <ol class="list-inside list-decimal space-y-2">
                                <li>
                                    <strong>Fresh period</strong>: cached data
                                    is returned immediately, no request made.
                                </li>
                                <li>
                                    <strong>Stale period</strong>: stale cached
                                    data is served instantly for immediate
                                    navigation. The page updates with fresh data
                                    from the server response.
                                </li>
                                <li>
                                    <strong>Expired</strong>: cache is gone, a
                                    full server request happens.
                                </li>
                            </ol>
                        </div>
                    </div>
                </FeatureCard>
            </div>
        </div>
    </AppLayout>
</template>
