<script setup lang="ts">
import { Head, router } from '@inertiajs/vue3';
import FeatureCard from '@/components/FeatureCard.vue';
import FeatureHeader from '@/components/FeatureHeader.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import AppLayout from '@/layouts/AppLayout.vue';
import { type BreadcrumbItem } from '@/types';

defineProps<{
    greeting?: string;
    serverTimestamp?: string;
    items?: Array<{ id: number; name: string }>;
}>();

const breadcrumbs: BreadcrumbItem[] = [
    { title: 'Navigation' },
    { title: 'Instant Visits' },
    { title: 'Target Page' },
];
</script>

<template>
    <Head title="Instant Visit Target" />

    <AppLayout :breadcrumbs="breadcrumbs">
        <div class="flex h-full flex-1 flex-col gap-6 p-4">
            <FeatureHeader
                title="Instant Visit Target"
                docs="the-basics/manual-visits#client-side-visits"
                controller="app/Http/Controllers/Feature/NavigationController.php#L87"
            >
                This page has an artificial server delay. With instant visits,
                the component renders immediately with placeholder props.
            </FeatureHeader>

            <div class="grid gap-6 lg:grid-cols-2">
                <FeatureCard
                    info-card
                    title="Server Data"
                    description="Props received from the server response."
                >
                    <div class="space-y-3">
                        <div class="flex items-center justify-between">
                            <span class="text-sm font-medium">Greeting</span>
                            <Badge variant="outline">{{
                                greeting ?? 'Loading...'
                            }}</Badge>
                        </div>
                        <div class="flex items-center justify-between">
                            <span class="text-sm font-medium">Timestamp</span>
                            <Badge
                                variant="outline"
                                class="font-mono text-xs"
                                >{{
                                    serverTimestamp ?? 'Waiting for server...'
                                }}</Badge
                            >
                        </div>
                    </div>
                </FeatureCard>

                <FeatureCard
                    info-card
                    title="Items List"
                    description="Items loaded from the server."
                >
                    <div v-if="items?.length" class="space-y-2">
                        <div
                            v-for="item in items"
                            :key="item.id"
                            class="flex items-center justify-between rounded-lg bg-muted/30 px-3 py-2"
                        >
                            <span class="text-sm">{{ item.name }}</span>
                            <Badge variant="secondary" class="text-xs"
                                >ID: {{ item.id }}</Badge
                            >
                        </div>
                    </div>
                    <div v-else class="space-y-2">
                        <div
                            v-for="i in 3"
                            :key="i"
                            class="h-10 animate-pulse rounded bg-muted"
                        />
                        <p class="text-xs text-muted-foreground">
                            Waiting for server data...
                        </p>
                    </div>
                </FeatureCard>

                <FeatureCard class="lg:col-span-2" title="Back to Source">
                    <Button
                        @click="
                            router.visit('/features/navigation/instant-visits')
                        "
                    >
                        Back to Instant Visits
                    </Button>
                </FeatureCard>
            </div>
        </div>
    </AppLayout>
</template>
