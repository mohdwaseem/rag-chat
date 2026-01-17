import { Component, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ButtonModule } from 'primeng/button';
import { TooltipModule } from 'primeng/tooltip';
import { ProgressBarModule } from 'primeng/progressbar';
import { ToastModule } from 'primeng/toast';
import { MessageService } from 'primeng/api';
import { ChatService } from '../../services/chat.service';
import { TranslateModule, TranslateService } from '@ngx-translate/core';

@Component({
  selector: 'app-document-upload',
  imports: [
    CommonModule,
    ButtonModule,
    TooltipModule,
    ProgressBarModule,
    ToastModule,
    TranslateModule
  ],
  providers: [MessageService],
  templateUrl: './document-upload.html',
  styleUrl: './document-upload.scss',
  standalone: true
})
export class DocumentUpload {
  @Output() documentUploaded = new EventEmitter<void>();

  isUploading: boolean = false;
  selectedFileName: string = '';

  constructor(
    private chatService: ChatService,
    private messageService: MessageService,
    private translate: TranslateService
  ) {}

  onFileSelected(event: any): void {
    const file: File = event.target.files[0];
    if (file) {
      if (file.type !== 'application/pdf') {
        this.messageService.add({
          severity: 'error',
          summary: this.translate.instant('documentUpload.invalidFileTitle'),
          detail: this.translate.instant('documentUpload.invalidFileMessage'),
          life: 3000
        });
        return;
      }

      this.selectedFileName = file.name;
      this.uploadFile(file);
    }
  }

  private uploadFile(file: File): void {
    this.isUploading = true;

    this.chatService.uploadDocument(file).subscribe({
      next: (response) => {
        this.isUploading = false;
        this.messageService.add({
          severity: 'success',
          summary: this.translate.instant('documentUpload.successTitle'),
          detail: response.message,
          life: 5000
        });
        this.documentUploaded.emit();
        this.selectedFileName = '';
      },
      error: (error) => {
        this.isUploading = false;
        console.error('Upload error:', error);
        this.messageService.add({
          severity: 'error',
          summary: 'Upload Failed',
          detail: this.translate.instant('documentUpload.uploadFailedMessage'),
          life: 5000
        });
        this.selectedFileName = '';
      }
    });
  }
}

