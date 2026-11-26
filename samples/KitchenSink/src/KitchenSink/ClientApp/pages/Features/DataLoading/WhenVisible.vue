<script setup lang="ts">
import { Head, WhenVisible } from '@inertiajs/vue3';
import FeatureCard from '@/components/FeatureCard.vue';
import FeatureHeader from '@/components/FeatureHeader.vue';
import { Badge } from '@/components/ui/badge';
import AppLayout from '@/layouts/AppLayout.vue';
import { type BreadcrumbItem } from '@/types';

defineProps<{
    section1?: Array<{ id: number; name: string }>;
    section2?: { totalContacts: number; generated: string };
    section3?: Array<{ id: number; name: string }>;
}>();

const breadcrumbs: BreadcrumbItem[] = [
    { title: 'Data Loading' },
    { title: 'When Visible' },
];
</script>

<template>
    <Head title="When Visible" />

    <AppLayout :breadcrumbs="breadcrumbs">
        <div class="flex h-full flex-1 flex-col gap-6 p-4">
            <FeatureHeader
                title="When Visible"
                docs="data-props/load-when-visible"
                controller="app/Http/Controllers/Feature/DataLoadingController.php#L70"
            >
                Viewport-triggered data loading using
                <code class="text-xs">Inertia::optional()</code> +
                <code class="text-xs">&lt;WhenVisible&gt;</code>.
            </FeatureHeader>

            <FeatureCard title="How It Works">
                <template #description>
                    Each section below uses
                    <code class="text-xs">Inertia::optional()</code> on the
                    server and
                    <code class="text-xs">&lt;WhenVisible&gt;</code> on the
                    client. Data is only fetched when the component scrolls into
                    the viewport.
                </template>
                <p class="text-sm text-muted-foreground">
                    Scroll down to trigger each section. Each has a simulated
                    server delay.
                </p>
            </FeatureCard>

            <!-- Spacer to force scrolling -->
            <div
                class="flex h-[75vh] items-center justify-center rounded-lg bg-gradient-to-b from-transparent to-muted/50"
            >
                <p class="animate-bounce text-sm text-muted-foreground">
                    &#8595; Scroll down to trigger lazy loading
                </p>
            </div>

            <!-- Section 1: Contact list (~500ms) -->
            <FeatureCard title="Recent Contacts">
                <WhenVisible data="section1">
                    <template #fallback>
                        <div class="space-y-2">
                            <div
                                v-for="i in 3"
                                :key="i"
                                class="h-8 animate-pulse rounded bg-muted"
                            />
                        </div>
                    </template>

                    <div class="space-y-2">
                        <div
                            v-for="contact in section1"
                            :key="contact.id"
                            class="flex items-center justify-between rounded bg-muted/50 px-3 py-2 text-sm"
                        >
                            <span>{{ contact.name }}</span>
                            <Badge variant="outline" class="text-xs"
                                >#{{ contact.id }}</Badge
                            >
                        </div>
                    </div>
                </WhenVisible>
            </FeatureCard>

            <!-- Another spacer -->
            <div
                class="flex h-[75vh] items-center justify-center rounded-lg bg-gradient-to-b from-transparent to-muted/50"
            >
                <p class="animate-bounce text-sm text-muted-foreground">
                    &#8595; Keep scrolling
                </p>
            </div>

            <!-- Section 2: Stats (~800ms) -->
            <FeatureCard title="Statistics">
                <template #description>
                    Loaded with <code class="text-xs">:buffer="200"</code>.
                    Starts loading 200px before visible.
                </template>
                <WhenVisible data="section2" :buffer="200">
                    <template #fallback>
                        <div class="flex gap-4">
                            <div
                                class="h-8 w-32 animate-pulse rounded bg-muted"
                            />
                            <div
                                class="h-8 w-48 animate-pulse rounded bg-muted"
                            />
                        </div>
                    </template>

                    <div class="flex flex-wrap gap-3">
                        <Badge variant="secondary"
                            >Total Contacts:
                            {{ section2?.totalContacts }}</Badge
                        >
                        <Badge variant="secondary"
                            >Generated: {{ section2?.generated }}</Badge
                        >
                    </div>
                </WhenVisible>
            </FeatureCard>

            <!-- Another spacer -->
            <div
                class="flex h-[75vh] items-center justify-center rounded-lg bg-gradient-to-b from-transparent to-muted/50"
            >
                <p class="animate-bounce text-sm text-muted-foreground">
                    &#8595; Almost there
                </p>
            </div>

            <!-- Section 3: Favorites (~600ms) -->
            <FeatureCard title="Favorite Contacts">
                <WhenVisible data="section3">
                    <template #fallback>
                        <div class="space-y-2">
                            <div
                                v-for="i in 5"
                                :key="i"
                                class="h-8 animate-pulse rounded bg-muted"
                            />
                        </div>
                    </template>

                    <div v-if="section3?.length" class="space-y-2">
                        <div
                            v-for="contact in section3"
                            :key="contact.id"
                            class="flex items-center justify-between rounded bg-muted/50 px-3 py-2 text-sm"
                        >
                            <span>{{ contact.name }}</span>
                            <span class="text-yellow-500">&#9733;</span>
                        </div>
                    </div>
                    <p v-else class="text-sm text-muted-foreground">
                        No favorite contacts found.
                    </p>
                </WhenVisible>
            </FeatureCard>
        </div>
    </AppLayout>
</template>
