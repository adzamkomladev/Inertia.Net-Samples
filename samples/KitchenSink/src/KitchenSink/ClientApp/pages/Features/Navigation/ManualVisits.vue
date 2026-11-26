<script setup lang="ts">
import { Head, router } from '@inertiajs/vue3';
import FeatureCard from '@/components/FeatureCard.vue';
import FeatureHeader from '@/components/FeatureHeader.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import AppLayout from '@/layouts/AppLayout.vue';
import { type BreadcrumbItem } from '@/types';

defineProps<{
    timestamp: string;
    counter: number;
}>();

const breadcrumbs: BreadcrumbItem[] = [
    { title: 'Navigation' },
    { title: 'Client-Side Visits' },
];

let pushCount = 0;

function pushUrl() {
    pushCount++;
    router.push({
        url: `/features/navigation/manual-visits?pushed=${pushCount}`,
    });
}

function replaceUrl() {
    router.replace({
        url: `/features/navigation/manual-visits?replaced=${Date.now()}`,
    });
}

function pushWithProps() {
    router.push({
        props: (currentProps: Record<string, unknown>) => ({
            ...currentProps,
            counter: (currentProps.counter as number) + 1,
        }),
    });
}

function pushWithPropCallback() {
    router.push({
        url: `/features/navigation/manual-visits?updated=${Date.now()}`,
        props: (currentProps: Record<string, unknown>) => ({
            ...currentProps,
            counter: (currentProps.counter as number) * 10,
            timestamp:
                'Client-side override at ' + new Date().toLocaleTimeString(),
        }),
    });
}
</script>

<template>
    <Head title="Client-Side Visits" />

    <AppLayout :breadcrumbs="breadcrumbs">
        <div class="flex h-full flex-1 flex-col gap-6 p-4">
            <FeatureHeader
                title="Client-Side Visits"
                docs="the-basics/manual-visits"
                controller="app/Http/Controllers/Feature/NavigationController.php#L103"
            >
                Client-side navigation using router.push() and router.replace().
                Update the URL and props without a server request.
            </FeatureHeader>

            <div class="grid gap-6 lg:grid-cols-2">
                <!-- Current State -->
                <FeatureCard
                    info-card
                    class="lg:col-span-2"
                    title="Current State"
                >
                    <div class="flex flex-wrap items-center gap-4">
                        <div class="flex items-center gap-2">
                            <span class="text-sm text-muted-foreground"
                                >URL:</span
                            >
                            <Badge
                                variant="outline"
                                class="font-mono text-xs"
                                >{{ $page.url }}</Badge
                            >
                        </div>
                        <div class="flex items-center gap-2">
                            <span class="text-sm text-muted-foreground"
                                >Timestamp:</span
                            >
                            <Badge variant="outline">{{ timestamp }}</Badge>
                        </div>
                        <div class="flex items-center gap-2">
                            <span class="text-sm text-muted-foreground"
                                >Counter:</span
                            >
                            <Badge
                                variant="outline"
                                class="min-w-8 justify-center tabular-nums"
                                >{{ counter }}</Badge
                            >
                        </div>
                    </div>
                </FeatureCard>

                <!-- URL Replacement -->
                <FeatureCard
                    title="URL Replacement"
                    description="Change the browser URL without making a server request."
                >
                    <div class="space-y-4">
                        <div class="space-y-2">
                            <h4 class="text-sm font-semibold">router.push()</h4>
                            <Button
                                variant="outline"
                                class="w-full"
                                @click="pushUrl()"
                            >
                                Push new URL
                            </Button>
                            <p class="text-xs text-muted-foreground">
                                Adds a new history entry. Press Back to return
                                to the previous URL.
                            </p>
                        </div>

                        <div class="space-y-2">
                            <h4 class="text-sm font-semibold">
                                router.replace()
                            </h4>
                            <Button
                                variant="outline"
                                class="w-full"
                                @click="replaceUrl()"
                            >
                                Replace current URL
                            </Button>
                            <p class="text-xs text-muted-foreground">
                                Overwrites the current history entry. Back skips
                                this URL.
                            </p>
                        </div>
                    </div>
                </FeatureCard>

                <!-- Prop Replacement -->
                <FeatureCard
                    title="Prop Replacement"
                    description="Update page props client-side. Use the callback form to spread existing props and override specific ones."
                >
                    <div class="space-y-4">
                        <div class="space-y-2">
                            <h4 class="text-sm font-semibold">
                                Increment a single prop
                            </h4>
                            <Button
                                variant="outline"
                                class="w-full"
                                @click="pushWithProps()"
                            >
                                Increment counter
                            </Button>
                            <p class="text-xs text-muted-foreground">
                                Spreads current props and overrides counter.
                                Other props stay unchanged.
                            </p>
                        </div>

                        <div class="space-y-2">
                            <h4 class="text-sm font-semibold">
                                Props callback
                            </h4>
                            <Button
                                variant="outline"
                                class="w-full"
                                @click="pushWithPropCallback()"
                            >
                                Transform props + URL
                            </Button>
                            <p class="text-xs text-muted-foreground">
                                Receives current props as an argument.
                                Multiplies the counter by 10 and overrides the
                                timestamp.
                            </p>
                        </div>
                    </div>
                </FeatureCard>
            </div>
        </div>
    </AppLayout>
</template>
