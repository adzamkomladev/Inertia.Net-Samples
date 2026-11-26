<script setup lang="ts">
import { Head, Link, router } from '@inertiajs/vue3';
import CodeBlock from '@/components/CodeBlock.vue';
import FeatureCard from '@/components/FeatureCard.vue';
import FeatureHeader from '@/components/FeatureHeader.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import AppLayout from '@/layouts/AppLayout.vue';
import { type BreadcrumbItem } from '@/types';
import { onceProps } from '@/wayfinder/App/Http/Controllers/Feature/DataLoadingController';

const props = defineProps<{
    page: number;
    staticData?: {
        generatedAt: string;
        randomId: number;
    };
    freshData?: {
        generatedAt: string;
        value: number;
    };
    expiringData?: {
        generatedAt: string;
        value: number;
    };
    aliasedData?: {
        generatedAt: string;
        value: number;
    };
    dynamicData: {
        timestamp: string;
        randomNumber: number;
    };
}>();

const breadcrumbs: BreadcrumbItem[] = [
    { title: 'Data Loading' },
    { title: 'Once Props' },
];

const otherPage = () => (props.page === 1 ? 2 : 1);
</script>

<template>
    <Head title="Once Props" />

    <AppLayout :breadcrumbs="breadcrumbs">
        <div class="flex h-full flex-1 flex-col gap-6 p-4">
            <FeatureHeader
                title="Once Props"
                docs="data-props/once-props"
                controller="app/Http/Controllers/Feature/DataLoadingController.php#L191"
            >
                Props that resolve once and are remembered by the client.
                Navigate between pages to see the once prop persist while
                dynamic props change.
            </FeatureHeader>

            <div class="grid gap-6 lg:grid-cols-2">
                <!-- Navigation -->
                <FeatureCard
                    info-card
                    class="lg:col-span-2"
                    title="Navigate Between Pages"
                >
                    <template #header-action>
                        <Badge variant="secondary"> Page {{ page }} </Badge>
                    </template>
                    <template #description>
                        Both pages return the same once props. Navigate between
                        them to see once props remembered while dynamic props
                        update. The server skips the callback entirely on
                        subsequent visits.
                    </template>
                    <div class="space-y-4">
                        <div class="flex flex-wrap gap-2">
                            <Button
                                :variant="page === 1 ? 'default' : 'outline'"
                                as-child
                            >
                                <Link :href="onceProps(1).url"> Page 1 </Link>
                            </Button>
                            <Button
                                :variant="page === 2 ? 'default' : 'outline'"
                                as-child
                            >
                                <Link :href="onceProps(2).url"> Page 2 </Link>
                            </Button>
                            <Button
                                variant="outline"
                                @click="
                                    router.reload({
                                        only: ['staticData', 'expiringData'],
                                    })
                                "
                            >
                                Force Refresh Once Prop
                            </Button>
                        </div>

                        <div
                            class="rounded-lg border border-black/5 bg-neutral-50/80 p-3 font-mono text-xs dark:border-white/5 dark:bg-neutral-900/80"
                        >
                            <ul class="list-inside list-disc space-y-1">
                                <li>
                                    Navigate to Page {{ otherPage() }} and back.
                                    The <strong>once prop</strong> keeps its
                                    original timestamp, the
                                    <strong>dynamic prop</strong> updates.
                                </li>
                                <li>
                                    <strong>Force Refresh</strong> uses
                                    <code
                                        >router.reload({ only: ['staticData',
                                        'expiringData'] })</code
                                    >
                                    to explicitly re-resolve once props.
                                </li>
                                <li>
                                    <strong>.fresh()</strong> re-evaluates on
                                    every visit.
                                    <strong>.until(5s)</strong> remembers for 5
                                    seconds.
                                </li>
                            </ul>
                        </div>
                    </div>
                </FeatureCard>

                <!-- Static (once) prop -->
                <FeatureCard title="Once Prop">
                    <template #description>
                        <code class="text-xs">Inertia::once(fn () => ...)</code
                        >. Resolved on first load, remembered across
                        navigations.
                    </template>
                    <div class="space-y-3">
                        <div class="flex items-center justify-between">
                            <span class="text-sm font-medium"
                                >Generated At</span
                            >
                            <Badge
                                variant="outline"
                                class="font-mono text-xs"
                                >{{ staticData?.generatedAt }}</Badge
                            >
                        </div>
                        <div class="flex items-center justify-between">
                            <span class="text-sm font-medium">Random ID</span>
                            <Badge variant="secondary">{{
                                staticData?.randomId
                            }}</Badge>
                        </div>
                    </div>
                </FeatureCard>

                <!-- Dynamic prop -->
                <FeatureCard
                    title="Dynamic Prop"
                    description="Regular prop, re-evaluated on every visit."
                >
                    <div class="space-y-3">
                        <div class="flex items-center justify-between">
                            <span class="text-sm font-medium">Timestamp</span>
                            <Badge
                                variant="outline"
                                class="font-mono text-xs"
                                >{{ dynamicData.timestamp }}</Badge
                            >
                        </div>
                        <div class="flex items-center justify-between">
                            <span class="text-sm font-medium"
                                >Random Number</span
                            >
                            <Badge variant="secondary">{{
                                dynamicData.randomNumber
                            }}</Badge>
                        </div>
                    </div>
                </FeatureCard>

                <!-- fresh() -->
                <FeatureCard title=".fresh()">
                    <template #description>
                        <code class="text-xs">Inertia::once(...)->fresh()</code
                        >. Forces re-evaluation on every visit.
                    </template>
                    <div class="space-y-3">
                        <div class="flex items-center justify-between">
                            <span class="text-sm font-medium"
                                >Generated At</span
                            >
                            <Badge
                                variant="outline"
                                class="font-mono text-xs"
                                >{{ freshData?.generatedAt ?? 'N/A' }}</Badge
                            >
                        </div>
                        <div class="flex items-center justify-between">
                            <span class="text-sm font-medium">Value</span>
                            <Badge variant="secondary">{{
                                freshData?.value ?? 'N/A'
                            }}</Badge>
                        </div>
                        <p class="text-xs text-muted-foreground">
                            Changes on every visit because
                            <code>.fresh()</code> forces re-resolution.
                        </p>
                    </div>
                </FeatureCard>

                <!-- until() -->
                <FeatureCard title=".until(5s)">
                    <template #description>
                        <code class="text-xs"
                            >Inertia::once(...)->until(now()->addSeconds(5))</code
                        >. Remembered for 5 seconds, then re-evaluated on the
                        next visit.
                    </template>
                    <div class="space-y-3">
                        <div class="flex items-center justify-between">
                            <span class="text-sm font-medium"
                                >Generated At</span
                            >
                            <Badge
                                variant="outline"
                                class="font-mono text-xs"
                                >{{ expiringData?.generatedAt ?? 'N/A' }}</Badge
                            >
                        </div>
                        <div class="flex items-center justify-between">
                            <span class="text-sm font-medium">Value</span>
                            <Badge variant="secondary">{{
                                expiringData?.value ?? 'N/A'
                            }}</Badge>
                        </div>
                        <p class="text-xs text-muted-foreground">
                            Cached for 5 seconds. After expiry, the next visit
                            re-evaluates the callback.
                        </p>
                    </div>
                </FeatureCard>

                <!-- as() -->
                <FeatureCard title=".as('key')">
                    <template #description>
                        <code class="text-xs"
                            >Inertia::once(...)->as('shared-once-key')</code
                        >. Custom storage key for cross-page sharing.
                    </template>
                    <div class="space-y-3">
                        <div class="flex items-center justify-between">
                            <span class="text-sm font-medium"
                                >Generated At</span
                            >
                            <Badge
                                variant="outline"
                                class="font-mono text-xs"
                                >{{ aliasedData?.generatedAt ?? 'N/A' }}</Badge
                            >
                        </div>
                        <div class="flex items-center justify-between">
                            <span class="text-sm font-medium">Value</span>
                            <Badge variant="secondary">{{
                                aliasedData?.value ?? 'N/A'
                            }}</Badge>
                        </div>
                        <p class="text-xs text-muted-foreground">
                            Multiple pages can share cached data under the same
                            custom key, even if their prop names differ.
                        </p>
                    </div>
                </FeatureCard>

                <!-- API Reference -->
                <FeatureCard info-card title="Fluent API">
                    <div class="space-y-3 text-xs">
                        <CodeBlock
                            code="
                            // Basic once prop
                            Inertia::once(fn () => ...);

                            // Always re-evaluate
                            Inertia::once(fn () => ...)->fresh();

                            // Expire after 5s
                            Inertia::once(fn () => ...)->until(now()->addSeconds(5));

                            // Custom cache key
                            Inertia::once(fn () => ...)->as('key');

                            // Shared once prop
                            Inertia::shareOnce('key', value);
                        "
                        />
                    </div>
                </FeatureCard>
            </div>
        </div>
    </AppLayout>
</template>
