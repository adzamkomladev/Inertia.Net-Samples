<script setup lang="ts">
import { Head, useForm } from '@inertiajs/vue3';
import CodeBlock from '@/components/CodeBlock.vue';
import FeatureCard from '@/components/FeatureCard.vue';
import FeatureHeader from '@/components/FeatureHeader.vue';
import InputError from '@/components/InputError.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import AppLayout from '@/layouts/AppLayout.vue';
import { type BreadcrumbItem } from '@/types';
import { submitUseForm } from '@/wayfinder/App/Http/Controllers/Feature/FormController';

const breadcrumbs: BreadcrumbItem[] = [
    { title: 'Forms' },
    { title: 'useForm' },
];

const form = useForm('use-form-demo', {
    name: '',
    email: '',
    bio: '',
    role: 'developer',
    subscribe: false,
});

function submit() {
    form.submit(submitUseForm(), {
        preserveScroll: true,
        onSuccess: () => form.defaults(),
    });
}

function submitWithTransform() {
    form.transform((data) => ({
        ...data,
        name: data.name.toUpperCase(),
    })).submit(submitUseForm(), {
        preserveScroll: true,
    });
}
</script>

<template>
    <Head title="useForm" />

    <AppLayout :breadcrumbs="breadcrumbs">
        <div class="flex h-full flex-1 flex-col gap-6 p-4">
            <FeatureHeader
                title="useForm"
                docs="the-basics/forms#form-helper"
                controller="app/Http/Controllers/Feature/FormController.php#L22"
            >
                Complete useForm API demonstration with all reactive state and
                methods.
            </FeatureHeader>

            <div class="grid gap-6 lg:grid-cols-2">
                <!-- Form -->
                <FeatureCard
                    title="Demo Form"
                    description="Fill out the form to see reactive state changes in real time."
                >
                    <form class="space-y-4" @submit.prevent="submit">
                        <div class="space-y-2">
                            <Label for="name">Name</Label>
                            <Input id="name" v-model="form.name" />
                            <InputError :message="form.errors.name" />
                        </div>

                        <div class="space-y-2">
                            <Label for="email">Email</Label>
                            <Input
                                id="email"
                                type="text"
                                v-model="form.email"
                            />
                            <InputError :message="form.errors.email" />
                        </div>

                        <div class="space-y-2">
                            <Label for="bio">Bio</Label>
                            <textarea
                                id="bio"
                                v-model="form.bio"
                                rows="3"
                                class="flex w-full rounded-md border border-input/60 bg-background px-3 py-2 text-sm placeholder:text-muted-foreground focus-visible:ring-1 focus-visible:ring-ring focus-visible:outline-none"
                                placeholder="Tell us about yourself..."
                            />
                            <InputError :message="form.errors.bio" />
                        </div>

                        <div class="space-y-2">
                            <Label for="role">Role</Label>
                            <select
                                id="role"
                                v-model="form.role"
                                class="flex h-9 w-full rounded-md border border-input/60 bg-background px-3 py-1 text-sm focus-visible:ring-1 focus-visible:ring-ring focus-visible:outline-none"
                            >
                                <option value="developer">Developer</option>
                                <option value="designer">Designer</option>
                                <option value="manager">Manager</option>
                                <option value="other">Other</option>
                            </select>
                            <InputError :message="form.errors.role" />
                        </div>

                        <div class="flex items-center gap-2">
                            <input
                                id="subscribe"
                                type="checkbox"
                                v-model="form.subscribe"
                                class="size-4 rounded border"
                            />
                            <Label for="subscribe"
                                >Subscribe to newsletter</Label
                            >
                        </div>

                        <!-- Upload progress -->
                        <div
                            v-if="form.progress"
                            class="w-full rounded-full bg-secondary"
                        >
                            <div
                                class="h-2 rounded-full bg-primary transition-all"
                                :style="{
                                    width: `${form.progress.percentage}%`,
                                }"
                            />
                        </div>

                        <!-- Submit -->
                        <div class="flex flex-wrap items-center gap-2 pt-2">
                            <Button type="submit" :disabled="form.processing">
                                {{
                                    form.processing ? 'Submitting...' : 'Submit'
                                }}
                            </Button>
                            <Button
                                type="button"
                                variant="secondary"
                                @click="submitWithTransform"
                            >
                                Submit with Transform
                            </Button>
                        </div>
                    </form>
                </FeatureCard>

                <!-- Reactive State Display -->
                <div class="space-y-6">
                    <FeatureCard
                        info-card
                        title="Reactive State"
                        description="These values update in real time as you interact with the form."
                    >
                        <div class="space-y-3">
                            <div class="flex items-center justify-between">
                                <span class="text-sm font-medium"
                                    >processing</span
                                >
                                <Badge
                                    :variant="
                                        form.processing
                                            ? 'default'
                                            : 'secondary'
                                    "
                                    >{{ form.processing }}</Badge
                                >
                            </div>
                            <div class="flex items-center justify-between">
                                <span class="text-sm font-medium">isDirty</span>
                                <Badge
                                    :variant="
                                        form.isDirty ? 'default' : 'secondary'
                                    "
                                    >{{ form.isDirty }}</Badge
                                >
                            </div>
                            <div class="flex items-center justify-between">
                                <span class="text-sm font-medium"
                                    >hasErrors</span
                                >
                                <Badge
                                    :variant="
                                        form.hasErrors
                                            ? 'destructive'
                                            : 'secondary'
                                    "
                                    >{{ form.hasErrors }}</Badge
                                >
                            </div>
                            <div class="flex items-center justify-between">
                                <span class="text-sm font-medium"
                                    >wasSuccessful</span
                                >
                                <Badge
                                    :variant="
                                        form.wasSuccessful
                                            ? 'default'
                                            : 'secondary'
                                    "
                                    >{{ form.wasSuccessful }}</Badge
                                >
                            </div>
                            <div class="flex items-center justify-between">
                                <span class="text-sm font-medium"
                                    >recentlySuccessful</span
                                >
                                <Badge
                                    :variant="
                                        form.recentlySuccessful
                                            ? 'default'
                                            : 'secondary'
                                    "
                                    >{{ form.recentlySuccessful }}</Badge
                                >
                            </div>
                            <div class="flex items-center justify-between">
                                <span class="text-sm font-medium"
                                    >progress</span
                                >
                                <Badge variant="secondary">{{
                                    form.progress?.percentage ?? 'null'
                                }}</Badge>
                            </div>
                        </div>
                    </FeatureCard>

                    <!-- Form Data -->
                    <FeatureCard
                        info-card
                        title="Form Data"
                        description="Current form values that will be submitted."
                    >
                        <CodeBlock
                            :code="JSON.stringify(form.data(), null, 2)"
                        />
                    </FeatureCard>

                    <!-- Errors -->
                    <FeatureCard info-card v-if="form.hasErrors" title="Errors">
                        <CodeBlock
                            :code="JSON.stringify(form.errors, null, 2)"
                        />
                    </FeatureCard>

                    <!-- Actions -->
                    <FeatureCard
                        info-card
                        title="Actions"
                        description="Methods available on the useForm instance."
                    >
                        <div class="flex flex-wrap gap-2">
                            <Button
                                variant="outline"
                                size="sm"
                                @click="form.reset()"
                            >
                                reset()
                            </Button>
                            <Button
                                variant="outline"
                                size="sm"
                                @click="form.clearErrors()"
                            >
                                clearErrors()
                            </Button>
                            <Button
                                variant="outline"
                                size="sm"
                                @click="form.cancel()"
                            >
                                cancel()
                            </Button>
                            <Button
                                variant="outline"
                                size="sm"
                                @click="form.defaults()"
                            >
                                defaults()
                            </Button>
                            <Button
                                variant="outline"
                                size="sm"
                                @click="
                                    form.setError(
                                        'name',
                                        'This is a manual error',
                                    )
                                "
                            >
                                setError('name', ...)
                            </Button>
                        </div>
                    </FeatureCard>
                </div>
            </div>
        </div>
    </AppLayout>
</template>
