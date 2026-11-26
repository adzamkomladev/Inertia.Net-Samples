<script setup lang="ts">
import { Head, Link, router } from '@inertiajs/vue3';
import FeatureCard from '@/components/FeatureCard.vue';
import FeatureHeader from '@/components/FeatureHeader.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import AppLayout from '@/layouts/AppLayout.vue';
import { type BreadcrumbItem } from '@/types';

const breadcrumbs: BreadcrumbItem[] = [
    { title: 'Prefetching' },
    { title: 'Cache Management' },
];
</script>

<template>
    <Head title="Cache Management" />

    <AppLayout :breadcrumbs="breadcrumbs">
        <div class="flex h-full flex-1 flex-col gap-6 p-4">
            <FeatureHeader
                title="Cache Management"
                docs="data-props/prefetching#cache-invalidation"
                controller="app/Http/Controllers/Feature/PrefetchingController.php#L25"
            >
                Tag-based cache system with targeted invalidation.
            </FeatureHeader>

            <div class="grid gap-6 lg:grid-cols-2">
                <!-- Cache Tags on Links -->
                <FeatureCard title="Cache Tags">
                    <template #description>
                        Tag prefetched data with
                        <code class="text-xs">cache-tags</code> for targeted
                        invalidation.
                    </template>
                    <div class="space-y-4">
                        <div class="space-y-2">
                            <h4 class="text-sm font-semibold">Tagged Links</h4>
                            <div class="flex flex-wrap gap-2">
                                <Link
                                    href="/contacts"
                                    prefetch
                                    cache-tags="crm"
                                    class="inline-flex items-center gap-1.5 rounded-md border border-black/10 bg-background px-3 py-1.5 text-sm hover:bg-accent dark:border-white/10"
                                >
                                    Contacts
                                    <Badge variant="outline" class="text-xs"
                                        >crm</Badge
                                    >
                                </Link>
                                <Link
                                    href="/organizations"
                                    prefetch
                                    :cache-tags="['crm', 'orgs']"
                                    class="inline-flex items-center gap-1.5 rounded-md border border-black/10 bg-background px-3 py-1.5 text-sm hover:bg-accent dark:border-white/10"
                                >
                                    Organizations
                                    <Badge variant="outline" class="text-xs"
                                        >crm, orgs</Badge
                                    >
                                </Link>
                                <Link
                                    href="/dashboard"
                                    prefetch
                                    cache-tags="dashboard"
                                    class="inline-flex items-center gap-1.5 rounded-md border border-black/10 bg-background px-3 py-1.5 text-sm hover:bg-accent dark:border-white/10"
                                >
                                    Dashboard
                                    <Badge variant="outline" class="text-xs"
                                        >dashboard</Badge
                                    >
                                </Link>
                            </div>
                            <p class="text-xs text-muted-foreground">
                                Hover to prefetch, then use the invalidation
                                buttons to flush by tag.
                            </p>
                        </div>
                    </div>
                </FeatureCard>

                <!-- Invalidation Controls -->
                <FeatureCard
                    title="Invalidation"
                    description="Flush cached data by tag or flush all."
                >
                    <div class="space-y-4">
                        <div class="space-y-2">
                            <h4 class="text-sm font-semibold">Flush by Tag</h4>
                            <div class="flex flex-wrap gap-2">
                                <Button
                                    variant="outline"
                                    size="sm"
                                    @click="router.flushByCacheTags('crm')"
                                >
                                    Flush "crm"
                                </Button>
                                <Button
                                    variant="outline"
                                    size="sm"
                                    @click="router.flushByCacheTags('orgs')"
                                >
                                    Flush "orgs"
                                </Button>
                                <Button
                                    variant="outline"
                                    size="sm"
                                    @click="
                                        router.flushByCacheTags('dashboard')
                                    "
                                >
                                    Flush "dashboard"
                                </Button>
                            </div>
                        </div>

                        <div class="space-y-2">
                            <h4 class="text-sm font-semibold">Flush All</h4>
                            <Button
                                variant="destructive"
                                size="sm"
                                @click="router.flushAll()"
                            >
                                Flush All Cache
                            </Button>
                        </div>

                        <div
                            class="rounded-lg border border-black/5 bg-neutral-50/80 p-3 font-mono text-xs dark:border-white/5 dark:bg-neutral-900/80"
                        >
                            <p>
                                <strong>Tip:</strong> Forms can auto-invalidate
                                cache on submission with
                                <code>:invalidate-cache-tags="['tag']"</code>.
                            </p>
                        </div>
                    </div>
                </FeatureCard>
            </div>
        </div>
    </AppLayout>
</template>
