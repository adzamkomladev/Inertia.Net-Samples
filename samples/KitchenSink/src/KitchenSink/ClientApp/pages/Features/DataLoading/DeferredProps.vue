<script setup lang="ts">
import { Head, Deferred, router } from '@inertiajs/vue3';
import FeatureCard from '@/components/FeatureCard.vue';
import FeatureHeader from '@/components/FeatureHeader.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import AppLayout from '@/layouts/AppLayout.vue';
import { type BreadcrumbItem } from '@/types';

defineProps<{
    quickStat: string;
    slowStats?: {
        totalContacts: number;
        totalFavorites: number;
    };
    heavyData?: Array<{ id: number; name: string }>;
    flakyReport?: { value: number };
}>();

const breadcrumbs: BreadcrumbItem[] = [
    { title: 'Data Loading' },
    { title: 'Deferred Props' },
];
</script>

<template>
    <Head title="Deferred Props" />

    <AppLayout :breadcrumbs="breadcrumbs">
        <div class="flex h-full flex-1 flex-col gap-6 p-4">
            <FeatureHeader
                title="Deferred Props"
                docs="data-props/deferred-props"
                controller="app/Http/Controllers/Feature/DataLoadingController.php#L16"
            >
                Lazy-loaded props with skeleton fallbacks. Expensive data loads
                after the initial page render.
            </FeatureHeader>

            <div class="grid gap-6 lg:grid-cols-3">
                <!-- Instant Prop -->
                <FeatureCard
                    title="Instant Prop"
                    description="Loaded immediately with the page response."
                >
                    <Badge>{{ quickStat }}</Badge>
                </FeatureCard>

                <!-- Deferred: slowStats (default group) -->
                <FeatureCard title="Deferred Stats">
                    <template #description>
                        <code class="text-xs">Inertia::defer()</code>. Default
                        group, ~800ms delay.
                    </template>
                    <Deferred data="slowStats">
                        <template #fallback>
                            <div class="space-y-3">
                                <div
                                    class="h-4 w-3/4 animate-pulse rounded bg-muted"
                                />
                                <div
                                    class="h-4 w-1/2 animate-pulse rounded bg-muted"
                                />
                            </div>
                        </template>
                        <div class="space-y-2">
                            <div class="flex items-center justify-between">
                                <span class="text-sm">Total Contacts</span>
                                <Badge variant="secondary">{{
                                    slowStats?.totalContacts
                                }}</Badge>
                            </div>
                            <div class="flex items-center justify-between">
                                <span class="text-sm">Total Favorites</span>
                                <Badge variant="secondary">{{
                                    slowStats?.totalFavorites
                                }}</Badge>
                            </div>
                        </div>
                    </Deferred>
                </FeatureCard>

                <!-- Deferred: heavyData (named group "heavy") -->
                <FeatureCard title="Heavy Data">
                    <template #description>
                        <code class="text-xs">Inertia::defer(fn, 'heavy')</code
                        >. Named group, ~1.5s delay.
                    </template>
                    <Deferred data="heavyData">
                        <template #fallback>
                            <div class="space-y-2">
                                <div
                                    v-for="i in 5"
                                    :key="i"
                                    class="h-4 animate-pulse rounded bg-muted"
                                    :style="{
                                        width: `${60 + Math.random() * 40}%`,
                                    }"
                                />
                            </div>
                        </template>
                        <div class="space-y-1.5">
                            <div
                                v-for="contact in heavyData"
                                :key="contact.id"
                                class="flex items-center justify-between rounded bg-muted/50 px-2 py-1 text-sm"
                            >
                                <span>{{ contact.name }}</span>
                                <Badge variant="outline" class="text-xs"
                                    >#{{ contact.id }}</Badge
                                >
                            </div>
                        </div>
                    </Deferred>
                </FeatureCard>
            </div>

            <div class="grid gap-6 lg:grid-cols-2">
                <!-- Reloading slot demo -->
                <FeatureCard title="Reloading Slot">
                    <template #description>
                        The <code class="text-xs">reloading</code> slot prop
                        lets you show stale data with a visual indicator while
                        refreshing.
                    </template>
                    <template #header-action>
                        <Button
                            variant="outline"
                            @click="router.reload({ only: ['slowStats'] })"
                        >
                            Reload Stats
                        </Button>
                    </template>
                    <Deferred data="slowStats">
                        <template #fallback>
                            <div class="space-y-3">
                                <div
                                    class="h-4 w-3/4 animate-pulse rounded bg-muted"
                                />
                                <div
                                    class="h-4 w-1/2 animate-pulse rounded bg-muted"
                                />
                            </div>
                        </template>
                        <template #default="{ reloading }">
                            <div
                                :class="{
                                    'opacity-50 transition-opacity': reloading,
                                }"
                                class="space-y-2"
                            >
                                <div class="flex items-center gap-2">
                                    <span class="text-sm"
                                        >Total Contacts:
                                        <strong>{{
                                            slowStats?.totalContacts
                                        }}</strong></span
                                    >
                                    <Badge
                                        v-if="reloading"
                                        variant="secondary"
                                        class="text-xs"
                                        >Refreshing...</Badge
                                    >
                                </div>
                                <div class="text-sm">
                                    Total Favorites:
                                    <strong>{{
                                        slowStats?.totalFavorites
                                    }}</strong>
                                </div>
                            </div>
                        </template>
                    </Deferred>
                </FeatureCard>

                <FeatureCard title="Rescued Deferred Prop" badge="v3.1">
                    <template #description>
                        <code class="text-xs"
                            >Inertia::defer(fn, rescue: true)</code
                        >. When the closure throws, the prop is marked rescued
                        and the <code class="text-xs">rescue</code> slot renders
                        instead of the fallback. Retry sends an
                        <code class="text-xs">X-Force-Success</code> header so
                        the server returns data instead of throwing.
                    </template>
                    <Deferred data="flakyReport">
                        <template #fallback>
                            <div class="space-y-3">
                                <div
                                    class="h-4 w-2/3 animate-pulse rounded bg-muted"
                                />
                                <div
                                    class="h-4 w-1/3 animate-pulse rounded bg-muted"
                                />
                            </div>
                        </template>
                        <template #rescue="{ reloading }">
                            <div
                                :class="{
                                    'opacity-50 transition-opacity': reloading,
                                }"
                                class="space-y-3 rounded border border-destructive/30 bg-destructive/5 p-3 text-sm text-destructive"
                            >
                                <div>
                                    <div class="font-medium">
                                        Failed to load report
                                    </div>
                                    <div class="text-xs opacity-80">
                                        Server threw during deferred resolution.
                                    </div>
                                </div>
                                <Button
                                    variant="outline"
                                    size="sm"
                                    :disabled="reloading"
                                    @click="
                                        router.reload({
                                            only: ['flakyReport'],
                                            headers: { 'X-Force-Success': '1' },
                                        })
                                    "
                                >
                                    {{ reloading ? 'Retrying...' : 'Retry' }}
                                </Button>
                            </div>
                        </template>
                        <div
                            class="rounded border border-emerald-500/30 bg-emerald-500/5 p-3 text-sm"
                        >
                            <div
                                class="font-medium text-emerald-700 dark:text-emerald-400"
                            >
                                Report loaded
                            </div>
                            <div class="text-xs">
                                Value: <strong>{{ flakyReport?.value }}</strong>
                            </div>
                        </div>
                    </Deferred>
                </FeatureCard>
            </div>
        </div>
    </AppLayout>
</template>
